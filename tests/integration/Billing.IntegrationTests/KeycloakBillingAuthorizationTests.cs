// <copyright file="KeycloakBillingAuthorizationTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using Billings.Application.Billing.Purchases;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
    private readonly ReservationHandler reservationHandler = new();
    private WebApplicationFactory<Program>? factory;
    private HttpClient? client;
    private string? connectionString;

    public async ValueTask InitializeAsync()
    {
        connectionString = await containers.CreateSharedTestDatabaseAsync(typeof(BillingDbContext), "Billing.Host").ConfigureAwait(false);
        factory = new BillingWebApplicationFactory(connectionString, containers.RabbitMqConnectionString, keycloak.Authority, reservationHandler);
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

    [Fact]
    public async Task InvoiceConfirmation_RequiresManagerRoleAndRejectsPublicReaders()
    {
        string readerToken = await GetTokenAsync(ReaderName, ReaderPassword).ConfigureAwait(false);
        string noReadToken = await GetTokenAsync(NoReadName, NoReadPassword).ConfigureAwait(false);
        var path = $"/purchases/{Guid.NewGuid():D}/confirm-invoice";
        var payload = new { InvoiceReference = "INV-test" };
        using var anonymous = await client!.PostAsJsonAsync(path, payload).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var reader = await SendJsonAsync(path, payload, readerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, reader.StatusCode);
        using var denied = await SendJsonAsync(path, payload, noReadToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    [Fact]
    public async Task PurgeFence_WithRealWorkerToken_DeniesBuyerAndReachesBillingGate()
    {
        var path = $"/internal/purge-fences/{Guid.NewGuid():D}/acquire";
        var payload = new { SubscriptionId = Guid.NewGuid(), SourceOrderId = Guid.NewGuid(), ApprovalId = Guid.NewGuid() };
        string buyerToken = await keycloak.GetUserTokenAsync(NoReadName, NoReadPassword, TokenClientId,
            clientSecret: TokenClientSecret, requestedScope: "billing-api-audience").ConfigureAwait(false);

        using var anonymous = await client!.PostAsJsonAsync(path, payload).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        using var buyer = await SendJsonAsync(path, payload, buyerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, buyer.StatusCode);

        using var tokenClient = new HttpClient();
        using var noScopeResponse = await tokenClient.PostAsync($"{keycloak.Authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "tenant-database-worker",
                ["client_secret"] = "local-only-test-tenant-database-worker-secret-not-for-production",
            })).ConfigureAwait(false);
        noScopeResponse.EnsureSuccessStatusCode();
        using JsonDocument noScopePayload = await JsonDocument.ParseAsync(
            await noScopeResponse.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        string noScopeToken = noScopePayload.RootElement.GetProperty("access_token").GetString()!;
        using var noScope = await SendJsonAsync(path, payload, noScopeToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, noScope.StatusCode);

        using var tokenResponse = await tokenClient.PostAsync($"{keycloak.Authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = "tenant-database-worker",
                ["client_secret"] = "local-only-test-tenant-database-worker-secret-not-for-production",
                ["scope"] = "billing:purge-fence",
            })).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();
        using JsonDocument tokenPayload = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        string workerToken = tokenPayload.RootElement.GetProperty("access_token").GetString()!;
        using var worker = await SendJsonAsync(path, payload, workerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Conflict, worker.StatusCode);
    }

    [Fact]
    public async Task PurchaseStatus_WithSignedNoOrganizationBuyerToken_ReachesOwnerLookup()
    {
        string buyerToken = await keycloak.GetUserTokenAsync(NoReadName, NoReadPassword, TokenClientId,
            clientSecret: TokenClientSecret, requestedScope: "billing-api-audience").ConfigureAwait(false);
        Assert.Contains(BillingResourceClientId, GetAudience(buyerToken));
        using var response = await SendAsync($"/purchases/{Guid.NewGuid():D}", buyerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task PurchaseStatus_BillingServiceTokenCannotActAsBuyer()
    {
        using var tokenClient = new HttpClient();
        using var tokenResponse = await tokenClient.PostAsync($"{keycloak.Authority}/protocol/openid-connect/token",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["grant_type"] = "client_credentials",
                ["client_id"] = BillingResourceClientId,
                ["client_secret"] = BillingResourceClientSecret,
            })).ConfigureAwait(false);
        tokenResponse.EnsureSuccessStatusCode();
        using JsonDocument payload = await JsonDocument.ParseAsync(
            await tokenResponse.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        string token = payload.RootElement.GetProperty("access_token").GetString()!;
        Assert.Contains(BillingResourceClientId, GetAudience(token));

        using var response = await SendAsync($"/purchases/{Guid.NewGuid():D}", token).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task PurchaseQuote_WithSignedNoOrganizationBuyerToken_RejectsOtherSubject()
    {
        string buyerToken = await keycloak.GetUserTokenAsync(NoReadName, NoReadPassword, TokenClientId,
            clientSecret: TokenClientSecret, requestedScope: "billing-api-audience").ConfigureAwait(false);
        using var payload = GetTokenPayload(buyerToken);
        reservationHandler.ReservationId = Guid.NewGuid();
        reservationHandler.OwnerSubjectId = payload.RootElement.GetProperty("sub").GetString()!;
        var quote = new { ReservationId = reservationHandler.ReservationId, PlanId = "Business", PlanVersion = "2026-10", DatabaseProvider = "postgres" };

        using var foreign = await SendJsonAsync("/purchases", quote,
            await GetTokenAsync(ReaderName, ReaderPassword).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        using var owned = await SendJsonAsync("/purchases", quote, buyerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, owned.StatusCode);
        using var created = await JsonDocument.ParseAsync(await owned.Content.ReadAsStreamAsync().ConfigureAwait(false)).ConfigureAwait(false);
        Assert.Equal(reservationHandler.ReservationId,
            created.RootElement.GetProperty("tenantId").GetGuid());
        using var status = await SendAsync($"/purchases/{reservationHandler.ReservationId:D}", buyerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, status.StatusCode);
    }

    [Fact]
    public async Task SalesQuote_WithSignedSalesRole_AuditsActorAndVerifiedOwner()
    {
        string ownerToken = await keycloak.GetUserTokenAsync(NoReadName, NoReadPassword, TokenClientId,
            clientSecret: TokenClientSecret, requestedScope: "billing-api-audience").ConfigureAwait(false);
        string salesToken = await keycloak.GetUserTokenAsync("integration-sales-user", "local-only-test-sales-password", TokenClientId,
            clientSecret: TokenClientSecret, requestedScope: "billing-api-audience").ConfigureAwait(false);
        using var owner = GetTokenPayload(ownerToken);
        using var seller = GetTokenPayload(salesToken);
        Assert.Contains(seller.RootElement.GetProperty("realm_access").GetProperty("roles").EnumerateArray(),
            role => role.GetString() == "sales");
        string ownerSubject = owner.RootElement.GetProperty("sub").GetString()!;
        string sellerSubject = seller.RootElement.GetProperty("sub").GetString()!;
        reservationHandler.ReservationId = Guid.NewGuid();
        reservationHandler.OwnerSubjectId = ownerSubject;
        var baseQuote = new { ReservationId = reservationHandler.ReservationId, PlanId = "Business", PlanVersion = "2026-10", DatabaseProvider = "postgres" };
        using var missingOwner = await SendJsonAsync("/purchases", baseQuote, salesToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, missingOwner.StatusCode);
        using var incorrectOwner = await SendJsonAsync("/purchases", new
        {
            baseQuote.ReservationId, baseQuote.PlanId, baseQuote.PlanVersion, baseQuote.DatabaseProvider,
            BeneficiarySubjectId = sellerSubject,
        }, salesToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, incorrectOwner.StatusCode);

        using var quoted = await SendJsonAsync("/purchases", new
        {
            baseQuote.ReservationId, baseQuote.PlanId, baseQuote.PlanVersion, baseQuote.DatabaseProvider,
            BeneficiarySubjectId = ownerSubject,
        }, salesToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, quoted.StatusCode);
        await using var scope = factory!.Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        var stored = await db.TenantPurchaseOrders.SingleAsync(item => item.TenantId == reservationHandler.ReservationId).ConfigureAwait(false);
        Assert.Equal(sellerSubject, stored.CreatedBySubjectId);
        Assert.Equal(ownerSubject, stored.BeneficiarySubjectId);
    }

    [Fact]
    public async Task InvoiceConfirmation_WithManagerToken_ReachesOrderLookupButReaderCannot()
    {
        string managerToken = await keycloak.GetUserTokenAsync("integration-manager", "local-only-test-manager-password",
            TokenClientId, clientSecret: TokenClientSecret, requestedScope: "billing-api-audience").ConfigureAwait(false);
        var path = $"/purchases/{Guid.NewGuid():D}/confirm-invoice";
        using var accepted = await SendJsonAsync(path, new { InvoiceReference = "INV-verified" }, managerToken).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, accepted.StatusCode);
        using var denied = await SendJsonAsync(path, new { InvoiceReference = "INV-unverified" },
            await GetTokenAsync(ReaderName, ReaderPassword).ConfigureAwait(false)).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);
    }

    private async Task<HttpResponseMessage> SendJsonAsync(string path, object payload, string token)
    {
        using var request = new HttpRequestMessage(HttpMethod.Post, path) { Content = System.Net.Http.Json.JsonContent.Create(payload) };
        request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        return await client!.SendAsync(request).ConfigureAwait(false);
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

    private sealed class ReservationHandler : HttpMessageHandler
    {
        internal Guid ReservationId { get; set; }

        internal string OwnerSubjectId { get; set; } = string.Empty;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath.EndsWith("/token", StringComparison.Ordinal))
            {
                return Task.FromResult(new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new { access_token = "isolated-reservation-service-token" }),
                });
            }

            var id = Guid.Parse(request.RequestUri.AbsolutePath.Split('/').Last());
            return Task.FromResult(id == ReservationId
                ? new HttpResponseMessage(HttpStatusCode.OK)
                {
                    Content = JsonContent.Create(new Billings.Host.Infrastructure.PurchaseReservationResponse
                    {
                        TenantId = ReservationId,
                        TenantIdentifier = $"keycloak-quote-{ReservationId:N}",
                        OwnerSubjectId = OwnerSubjectId,
                        Status = "pending",
                    }),
                }
                : new HttpResponseMessage(HttpStatusCode.NotFound));
        }
    }

    private sealed class BillingWebApplicationFactory(string databaseConnectionString, string rabbitMqConnectionString, string authority, ReservationHandler reservationHandler) : WebApplicationFactory<Program>
    {
        private const string PaymentPassphrase = "test-only-keycloak-billing-payment-proof";
        private static readonly Lazy<string> PaymentPrivateKey = new(() =>
        {
            using RSA key = RSA.Create(2048);
            return key.ExportEncryptedPkcs8PrivateKeyPem(PaymentPassphrase,
                new PbeParameters(PbeEncryptionAlgorithm.Aes256Cbc, HashAlgorithmName.SHA256, 10000));
        });
        static BillingWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:BillingWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:BillingRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitMqConnectionString);
            builder.UseSetting("PaymentProof:EncryptedPrivateKey", PaymentPrivateKey.Value);
            builder.UseSetting("PaymentProof:Passphrase", PaymentPassphrase);
            builder.UseSetting("Keycloak:realm", KeycloakTestRealmFixture.Realm);
            builder.UseSetting("Keycloak:auth-server-url", authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)]);
            builder.UseSetting("Keycloak:resource", BillingResourceClientId);
            builder.UseSetting("Keycloak:credentials:secret", BillingResourceClientSecret);
            builder.ConfigureTestServices(services =>
            {
                services.AddHttpClient<Billings.Host.Infrastructure.CustomerReservationClient>()
                    .ConfigurePrimaryHttpMessageHandler(() => reservationHandler);
                services.RemoveAll<IStripePurchaseGateway>();
                services.AddSingleton<RecordingStripePurchaseGateway>();
                services.AddSingleton<IStripePurchaseGateway>(provider => provider.GetRequiredService<RecordingStripePurchaseGateway>());
                services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
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
                    });
            });
        }
    }
}
