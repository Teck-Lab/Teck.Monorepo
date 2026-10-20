// <copyright file="KeycloakBasketAuthorizationTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Baskets.Application.Baskets.Responses;
using Baskets.Application.Database;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Baskets.IntegrationTests;

/// <summary>Exercises Basket.Host authorization with real Keycloak tokens and real infrastructure.</summary>
[Collection("KeycloakTestRealm")]
public sealed class KeycloakBasketAuthorizationTests(KeycloakTestRealmFixture keycloak, SharedTestcontainersFixture containers) : IAsyncLifetime
{
    private const string ClientId = "integration-test-client";
    private const string ClientSecret = "local-only-test-integration-token-client-secret-not-for-production";
    private const string BasketClientId = "basket-api";
    private const string BasketClientSecret = "local-only-test-basket-api-secret-not-for-production";
    private const string Manager = "integration-manager";
    private const string ManagerPassword = "local-only-test-manager-password";
    private const string Reader = "integration-reader";
    private const string ReaderPassword = "local-only-test-reader-password";
    private string? connectionString;
    private WebApplicationFactory<Program>? factory;
    private HttpClient? client;

    public async ValueTask InitializeAsync()
    {
        connectionString = await containers.CreateSharedTestDatabaseAsync(typeof(BasketDbContext), "Basket.Host").ConfigureAwait(false);
        factory = new BasketKeycloakWebApplicationFactory(connectionString, containers.RabbitMqConnectionString, keycloak.Authority, keycloak.OrganizationId);
        client = factory.CreateClient();
    }

    [Fact]
    public async Task Checkout_RequiresRealKeycloakPermissionAndEnforcesTenantSubjectOwnership()
    {
        client!.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage missing = await client.PostAsJsonAsync("/baskets/checkout", new { BasketId = Guid.NewGuid(), AuthorizedAmount = 0m, Currency = "USD", PaymentReference = "tok_test" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, missing.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using HttpResponseMessage invalid = await client.PostAsJsonAsync("/baskets/checkout", new { BasketId = Guid.NewGuid(), AuthorizedAmount = 0m, Currency = "USD", PaymentReference = "tok_test" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);

        string readerToken = await keycloak.GetUserTokenAsync(Reader, ReaderPassword, ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerToken);
        BasketDto readerBasket = await client.GetFromJsonAsync<BasketDto>("/baskets/current").ConfigureAwait(false)
            ?? throw new InvalidOperationException("Anonymous current endpoint returned no basket.");
        using HttpResponseMessage denied = await client.PostAsJsonAsync("/baskets/checkout", new { BasketId = readerBasket.Id, AuthorizedAmount = 0m, Currency = "USD", PaymentReference = "tok_test" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, denied.StatusCode);

        string managerToken = await keycloak.GetUserTokenAsync(Manager, ManagerPassword, ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        AssertAudience(managerToken);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        BasketDto basket = await client.GetFromJsonAsync<BasketDto>("/baskets/current").ConfigureAwait(false)
            ?? throw new InvalidOperationException("Anonymous current endpoint returned no basket.");
        using HttpResponseMessage add = await client.PostAsJsonAsync("/baskets/items", new { BasketId = basket.Id, ProductId = Guid.NewGuid(), ProductName = "Widget", Quantity = 1 }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        string foreignToken = await keycloak.GetUserTokenAsync("integration-foreign-manager", "local-only-test-foreign-manager-password", ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        using (JsonDocument foreignPayload = ReadTokenPayload(foreignToken))
        {
            Assert.Contains(keycloak.ForeignOrganizationId, foreignPayload.RootElement.GetProperty("organization")
                .EnumerateObject().Select(value => value.Value.GetProperty("id").GetString()));
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignToken);
        using HttpResponseMessage foreignOwnBasket = await client.GetAsync("/baskets/current").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, foreignOwnBasket.StatusCode);
        using HttpResponseMessage foreignCheckout = await client.PostAsJsonAsync("/baskets/checkout", new { BasketId = basket.Id, AuthorizedAmount = 100m, Currency = "USD", PaymentReference = "tok_foreign" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.NotFound, foreignCheckout.StatusCode);
        await using (var unchangedDatabase = new BasketDbContext(new DbContextOptionsBuilder<BasketDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(keycloak.OrganizationId)
            .Options, null!))
        {
            var unchanged = await unchangedDatabase.Baskets.SingleAsync(value => value.Id == basket.Id).ConfigureAwait(false);
            Assert.Equal("Active", unchanged.Status.ToString());
            Assert.Null(unchanged.PaymentReference);
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        using HttpResponseMessage checkout = await client.PostAsJsonAsync("/baskets/checkout", new { BasketId = basket.Id, AuthorizedAmount = 100m, Currency = "USD", PaymentReference = "tok_keycloak_test" }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Accepted, checkout.StatusCode);
        BasketDto result = await checkout.Content.ReadFromJsonAsync<BasketDto>().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Checkout returned no basket.");
        Assert.Equal("PricingPending", result.Status);
        Assert.Equal(basket.Id, result.Id);

        await using var database = new BasketDbContext(new DbContextOptionsBuilder<BasketDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(keycloak.OrganizationId)
            .Options, null!);
        var persisted = await database.Baskets.SingleAsync(value => value.Id == basket.Id).ConfigureAwait(false);
        Assert.Equal(keycloak.OrganizationId, persisted.TenantId);
        Assert.Equal(ReadSubject(managerToken), persisted.Subject);
        Assert.Equal("PricingPending", persisted.Status.ToString());
        Assert.Equal("tok_keycloak_test", persisted.PaymentReference);
    }

    [Fact]
    public async Task Merge_RequiresRealKeycloakPermissionAndPreservesGuestOwnership()
    {
        string managerToken = await keycloak.GetUserTokenAsync(Manager, ManagerPassword, ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        string readerToken = await keycloak.GetUserTokenAsync(Reader, ReaderPassword, ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        client!.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage guestResponse = await client.GetAsync("/baskets/current").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, guestResponse.StatusCode);
        BasketDto guest = await guestResponse.Content.ReadFromJsonAsync<BasketDto>().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Anonymous current endpoint returned no basket.");
        Assert.NotNull(guest.AnonymousToken);
        client.DefaultRequestHeaders.Add("X-Basket-Token", guest.AnonymousToken.Value.ToString());
        using HttpResponseMessage add = await client.PostAsJsonAsync("/baskets/items", new { BasketId = guest.Id, ProductId = Guid.NewGuid(), ProductName = "Guest item", Quantity = 1 }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerToken);
        using HttpResponseMessage foreign = await client.PostAsJsonAsync("/baskets/merge", new { AnonymousToken = guest.AnonymousToken }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, foreign.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        using HttpResponseMessage merged = await client.PostAsJsonAsync("/baskets/merge", new { AnonymousToken = guest.AnonymousToken }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, merged.StatusCode);
        BasketDto result = await merged.Content.ReadFromJsonAsync<BasketDto>().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Merge returned no basket.");
        Assert.Equal(ReadSubject(managerToken), result.Subject);
        Assert.Single(result.Items);
    }

    [Fact]
    public async Task CurrentAndItemEndpointsRemainAnonymous()
    {
        client!.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage current = await client.GetAsync("/baskets/current").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, current.StatusCode);
        BasketDto basket = await current.Content.ReadFromJsonAsync<BasketDto>().ConfigureAwait(false)
            ?? throw new InvalidOperationException("Anonymous current endpoint returned no basket.");
        client.DefaultRequestHeaders.Add("X-Basket-Token", basket.AnonymousToken!.Value.ToString());
        using HttpResponseMessage add = await client.PostAsJsonAsync("/baskets/items", new { BasketId = basket.Id, ProductId = Guid.NewGuid(), ProductName = "Anonymous item", Quantity = 1 }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, add.StatusCode);
        client.DefaultRequestHeaders.Remove("X-Basket-Token");
        using HttpResponseMessage otherGuest = await client.PostAsJsonAsync("/baskets/items", new { BasketId = basket.Id, ProductId = Guid.NewGuid(), ProductName = "Other guest", Quantity = 1 }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, otherGuest.StatusCode);
        await using var database = new BasketDbContext(new DbContextOptionsBuilder<BasketDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(keycloak.OrganizationId)
            .Options, null!);
        var persisted = await database.Baskets.Include(value => value.Items).SingleAsync(value => value.Id == basket.Id).ConfigureAwait(false);
        Assert.Single(persisted.Items);
    }

    public async ValueTask DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null) await factory.DisposeAsync().ConfigureAwait(false);
        if (connectionString is not null) await containers.TruncateAllTablesAsync(connectionString).ConfigureAwait(false);
    }

    private void AssertAudience(string token)
    {
        using JsonDocument payload = ReadTokenPayload(token);
        JsonElement audience = payload.RootElement.GetProperty("aud");
        Assert.True(audience.ValueKind == JsonValueKind.String
            ? audience.GetString() == BasketClientId
            : audience.EnumerateArray().Any(value => value.GetString() == BasketClientId), "The signed Keycloak token must include basket-api as audience.");
    }

    private static string ReadSubject(string token)
    {
        using JsonDocument payload = ReadTokenPayload(token);
        return payload.RootElement.GetProperty("sub").GetString()!;
    }

    private static JsonDocument ReadTokenPayload(string token)
    {
        string payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return JsonDocument.Parse(Convert.FromBase64String(payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=')));
    }

    private sealed class BasketKeycloakWebApplicationFactory(string database, string rabbitMq, string authority, string storefrontTenantId) : WebApplicationFactory<Program>
    {
        static BasketKeycloakWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:BasketWrite", database);
            builder.UseSetting("ConnectionStrings:BasketRead", database);
            builder.UseSetting("ConnectionStrings:Default", database);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitMq);
            builder.UseSetting("Keycloak:realm", KeycloakTestRealmFixture.Realm);
            builder.UseSetting("Keycloak:auth-server-url", authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)]);
            builder.UseSetting("Keycloak:resource", BasketClientId);
            builder.UseSetting("Keycloak:credentials:secret", BasketClientSecret);
            builder.UseSetting("Basket:StorefrontTenantId", storefrontTenantId);
            builder.ConfigureTestServices(services =>
            {
                services.PostConfigure<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme, options =>
                {
                    options.Authority = authority;
                    options.MetadataAddress = $"{authority}/.well-known/openid-configuration";
                    options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                        options.MetadataAddress,
                        new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfigurationRetriever(),
                        new Microsoft.IdentityModel.Protocols.HttpDocumentRetriever { RequireHttps = false });
                    options.Audience = BasketClientId;
                    options.RequireHttpsMetadata = false;
                });
            });
        }
    }
}
