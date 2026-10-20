// <copyright file="KeycloakBillingAuthorizationTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SharedKernel.Events;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Exercises direct Billing.Host authorization with real Keycloak tokens and PostgreSQL.</summary>
[Collection("KeycloakTestRealm")]
public sealed class KeycloakBillingAuthorizationTests(KeycloakTestRealmFixture keycloak, SharedTestcontainersFixture containers) : IAsyncLifetime
{
    private const string TokenClientId = "integration-test-client";
    private const string TokenClientSecret = "local-only-test-integration-token-client-secret-not-for-production";
    private const string BillingResourceClientId = "billing-api";
    private const string BillingResourceClientSecret = "local-only-test-billing-api-secret-not-for-production";
    private const string ReaderName = "integration-reader";
    private const string ReaderPassword = "local-only-test-reader-password";
    private const string NoReadName = "integration-no-access";
    private const string NoReadPassword = "local-only-test-no-access-password";
    private WebApplicationFactory<Program>? factory;
    private HttpClient? client;
    private string? connectionString;

    public async ValueTask InitializeAsync()
    {
        connectionString = await containers.CreateSharedTestDatabaseAsync(typeof(BillingDbContext), "Billing.Host").ConfigureAwait(false);
        factory = new BillingWebApplicationFactory(connectionString, containers.RabbitMqConnectionString, keycloak.Authority);
        client = factory.CreateClient();
    }

    [Fact]
    public async Task BillingReadEndpoints_WithRealKeycloakTokens_EnforceAuthenticationPermissionAndTenantScope()
    {
        string readerToken = await GetTokenAsync(ReaderName, ReaderPassword).ConfigureAwait(false);
        string noReadToken = await GetTokenAsync(NoReadName, NoReadPassword).ConfigureAwait(false);
        string organizationId = GetOrganizationId(readerToken);
        Assert.Equal(keycloak.OrganizationId, organizationId);
        Assert.Equal(keycloak.Authority, GetIssuer(readerToken));
        Assert.Contains(BillingResourceClientId, GetAudience(readerToken));
        string wrongAudienceToken = await keycloak.GetUserTokenAsync(ReaderName, ReaderPassword, TokenClientId,
            clientSecret: TokenClientSecret, requestedScope: "order-api-audience").ConfigureAwait(false);
        Assert.DoesNotContain(BillingResourceClientId, GetAudience(wrongAudienceToken));
        using HttpResponseMessage wrongAudience = await SendAsync("/payments", wrongAudienceToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);

        (Guid paymentId, Guid invoiceId) = await SeedCapturedOrderAsync(organizationId).ConfigureAwait(false);
        (Guid foreignPaymentId, Guid foreignInvoiceId) = await SeedCapturedOrderAsync("billing-other-tenant").ConfigureAwait(false);

        foreach (string path in new[] { "/payments", $"/payments/{paymentId}", $"/invoices/{invoiceId}" })
        {
            using HttpResponseMessage anonymous = await SendAsync(path, null).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
            using HttpResponseMessage invalid = await SendAsync(path, "not-a-jwt").ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
            using HttpResponseMessage denied = await SendAsync(path, noReadToken).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
        }

        using HttpResponseMessage list = await SendAsync("/payments", readerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, list.StatusCode);
        using JsonDocument payments = JsonDocument.Parse(await list.Content.ReadAsStreamAsync().ConfigureAwait(false));
        Guid[] paymentIds = payments.RootElement.EnumerateArray().Select(payment => payment.GetProperty("id").GetGuid()).ToArray();
        Assert.Contains(paymentId, paymentIds);
        Assert.DoesNotContain(foreignPaymentId, paymentIds);

        using HttpResponseMessage ownPayment = await SendAsync($"/payments/{paymentId}", readerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, ownPayment.StatusCode);
        using JsonDocument payment = JsonDocument.Parse(await ownPayment.Content.ReadAsStreamAsync().ConfigureAwait(false));
        Assert.Equal(paymentId, payment.RootElement.GetProperty("id").GetGuid());
        using HttpResponseMessage foreignPayment = await SendAsync($"/payments/{foreignPaymentId}", readerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, foreignPayment.StatusCode);
        using HttpResponseMessage ownInvoice = await SendAsync($"/invoices/{invoiceId}", readerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, ownInvoice.StatusCode);
        using JsonDocument invoice = JsonDocument.Parse(await ownInvoice.Content.ReadAsStreamAsync().ConfigureAwait(false));
        Assert.Equal(invoiceId, invoice.RootElement.GetProperty("id").GetGuid());
        using HttpResponseMessage foreignInvoice = await SendAsync($"/invoices/{foreignInvoiceId}", readerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, foreignInvoice.StatusCode);
    }

    public async ValueTask DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null)
        {
            await factory.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task<string> GetTokenAsync(string username, string password) =>
        await keycloak.GetUserTokenAsync(username, password, TokenClientId, clientSecret: TokenClientSecret).ConfigureAwait(false);

    private async Task<(Guid PaymentId, Guid InvoiceId)> SeedCapturedOrderAsync(string tenantId)
    {
        Guid orderId = Guid.NewGuid();
        var message = new OrderPlacedV2IntegrationEvent
        {
            OrderId = orderId,
            BasketId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            KeycloakSubjectId = "keycloak-billing-integration-test",
            TenantId = tenantId,
            Amount = 42.50m,
            AuthorizedAmount = 50m,
            Currency = "USD",
            PaymentMethodToken = "pm_billing_integration_test",
            RequestId = $"billing-authz-{Guid.NewGuid():N}",
            SourceCorrelationId = $"billing-authz-{Guid.NewGuid():N}",
            CreatedAt = DateTimeOffset.UtcNow,
        };

        using (IServiceScope scope = factory!.Services.CreateScope())
        {
            await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                .InvokeForTenantAsync(tenantId, message, CancellationToken.None).ConfigureAwait(false);
        }

        await using var verifyScope = factory!.Services.CreateAsyncScope();
        verifyScope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext =
            new MultiTenantContext<TenantDetails>(new TenantDetails
            {
                Id = tenantId,
                Identifier = tenantId,
                Name = tenantId,
                IsActive = true,
            });
        var database = verifyScope.ServiceProvider.GetRequiredService<BillingDbContext>();
        Payment payment = await database.Payments.SingleAsync(candidate => candidate.OrderId == orderId).ConfigureAwait(false);
        Invoice invoice = await database.Invoices.SingleAsync(candidate => candidate.OrderId == orderId).ConfigureAwait(false);
        return (payment.Id, invoice.Id);
    }

    private Task<HttpResponseMessage> SendAsync(string path, string? token)
    {
        client!.DefaultRequestHeaders.Authorization = token is null ? null : new AuthenticationHeaderValue("Bearer", token);
        return client.GetAsync(path);
    }

    private static string GetIssuer(string token)
    {
        using JsonDocument payload = GetTokenPayload(token);
        return payload.RootElement.GetProperty("iss").GetString()!;
    }

    private static JsonDocument GetTokenPayload(string token)
    {
        string[] segments = token.Split('.');
        string payload = segments[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload));
    }

    private static string GetOrganizationId(string token)
    {
        using JsonDocument payload = GetTokenPayload(token);
        JsonElement memberships = payload.RootElement.GetProperty("organization");
        return memberships.EnumerateObject()
            .Select(property => property.Value.GetProperty("id").GetString())
            .Single(id => !string.IsNullOrWhiteSpace(id))!;
    }

    private static IEnumerable<string> GetAudience(string token)
    {
        using JsonDocument payload = GetTokenPayload(token);
        JsonElement audience = payload.RootElement.GetProperty("aud");
        return audience.ValueKind == JsonValueKind.String
            ? [audience.GetString()!]
            : audience.EnumerateArray().Select(value => value.GetString()!).ToArray();
    }

    private sealed class BillingWebApplicationFactory(string databaseConnectionString, string rabbitMqConnectionString, string authority) : WebApplicationFactory<Program>
    {
        static BillingWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:BillingWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:BillingRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitMqConnectionString);
            builder.UseSetting("Keycloak:realm", KeycloakTestRealmFixture.Realm);
            builder.UseSetting("Keycloak:auth-server-url", authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)]);
            builder.UseSetting("Keycloak:resource", BillingResourceClientId);
            builder.UseSetting("Keycloak:credentials:secret", BillingResourceClientSecret);
            builder.ConfigureTestServices(services => services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.Authority = authority;
                    options.MetadataAddress = $"{authority}/.well-known/openid-configuration";
                    options.RequireHttpsMetadata = false;
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        options.MetadataAddress,
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever { RequireHttps = false });
                }));
        }
    }
}
