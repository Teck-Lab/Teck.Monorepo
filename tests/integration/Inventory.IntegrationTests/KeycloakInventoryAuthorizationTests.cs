// <copyright file="KeycloakInventoryAuthorizationTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using Inventories.Application.Database;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Inventories.IntegrationTests;

/// <summary>Shares real Keycloak, PostgreSQL, and RabbitMQ containers for Inventory authorization tests.</summary>
[CollectionDefinition("KeycloakTestRealm")]
public sealed class KeycloakInventoryTestRealmCollection : ICollectionFixture<KeycloakTestRealmFixture>, ICollectionFixture<SharedTestcontainersFixture>;

/// <summary>Exercises direct Inventory.Host authorization with real Keycloak and PostgreSQL.</summary>
[Collection("KeycloakTestRealm")]
public sealed class KeycloakInventoryAuthorizationTests(KeycloakTestRealmFixture keycloak, SharedTestcontainersFixture containers) : IAsyncLifetime
{
    private const string ClientId = "integration-test-client";
    private const string ClientSecret = "local-only-test-integration-token-client-secret-not-for-production";
    private const string InventoryResourceClientSecret = "local-only-test-inventory-api-secret-not-for-production";
    private string? connectionString;
    private InventoryWebApplicationFactory? factory;
    private HttpClient? client;

    public async ValueTask InitializeAsync()
    {
        connectionString = await containers.CreateSharedTestDatabaseAsync(typeof(InventoryDbContext), "Inventory.Host").ConfigureAwait(false);
        factory = new InventoryWebApplicationFactory(connectionString, containers.RabbitMqConnectionString, keycloak.Authority);
        client = factory.CreateClient();
    }

    [Fact]
    public async Task InventoryEndpoints_WithRealKeycloakPermissions_EnforceScopesAndPersistTenantState()
    {
        string readerToken = await keycloak.GetUserTokenAsync("integration-reader", "local-only-test-reader-password", ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        string managerToken = await keycloak.GetUserTokenAsync("integration-manager", "local-only-test-manager-password", ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        using JsonDocument readerPayload = ReadTokenPayload(readerToken);
        using JsonDocument managerPayload = ReadTokenPayload(managerToken);
        AssertOrganizationAudience(readerPayload.RootElement);
        AssertOrganizationAudience(managerPayload.RootElement);

        string organizationId = keycloak.OrganizationId;
        Guid productId = Guid.NewGuid();
        Guid locationId = Guid.NewGuid();
        Guid stockItemId;
        var databaseOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(organizationId)
            .Options;
        await using (var database = new InventoryDbContext(databaseOptions, null!))
        {
            StockItem stockItem = StockItem.Create(productId, locationId, organizationId, 8, allowBackorder: false, reorderThreshold: 1);
            database.StockItems.Add(stockItem);
            await database.SaveChangesAsync().ConfigureAwait(false);
            stockItemId = stockItem.Id;
        }

        using HttpResponseMessage anonymous = await client!.GetAsync($"/inventory/availability?productId={productId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymous.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-signed-jwt");
        using HttpResponseMessage invalid = await client.GetAsync($"/inventory/availability?productId={productId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, invalid.StatusCode);
        string wrongAudienceToken = await keycloak.GetUserTokenAsync("integration-manager", "local-only-test-manager-password", ClientId, clientSecret: ClientSecret, requestedScope: "order-api-audience").ConfigureAwait(false);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", wrongAudienceToken);
        using HttpResponseMessage wrongAudience = await client.GetAsync($"/inventory/availability?productId={productId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, wrongAudience.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerToken);
        using HttpResponseMessage allowedRead = await client.GetAsync($"/inventory/availability?productId={productId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, allowedRead.StatusCode);
        AvailabilityDto? availability = await allowedRead.Content.ReadFromJsonAsync<AvailabilityDto>().ConfigureAwait(false);
        Assert.NotNull(availability);
        Assert.Equal(productId, availability.ProductId);
        Assert.Equal(8, availability.Available);

        using HttpResponseMessage deniedAdjust = await client.PostAsJsonAsync($"/inventory/stock-items/{stockItemId}/adjust", new { Id = stockItemId, Delta = -2 }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, deniedAdjust.StatusCode);
        using HttpResponseMessage deniedRegister = await client.PostAsJsonAsync("/inventory/stock-items", new
        {
            ProductId = Guid.NewGuid(),
            LocationId = Guid.NewGuid(),
            QuantityOnHand = 3,
            AllowBackorder = false,
            ReorderThreshold = 0,
        }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, deniedRegister.StatusCode);
        using HttpResponseMessage deniedPolicy = await client.PutAsJsonAsync($"/inventory/stock-items/{stockItemId}/policy", new { Id = stockItemId, AllowBackorder = true, ReorderThreshold = 4 }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, deniedPolicy.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        Guid registeredProductId = Guid.NewGuid();
        using HttpResponseMessage allowedRegister = await client.PostAsJsonAsync("/inventory/stock-items", new
        {
            ProductId = registeredProductId,
            LocationId = Guid.NewGuid(),
            QuantityOnHand = 3,
            AllowBackorder = false,
            ReorderThreshold = 0,
        }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, allowedRegister.StatusCode);
        StockItemDto? registered = await allowedRegister.Content.ReadFromJsonAsync<StockItemDto>().ConfigureAwait(false);
        Assert.NotNull(registered);
        Assert.Equal(registeredProductId, registered.ProductId);
        await using (var database = new InventoryDbContext(databaseOptions, null!))
        {
            Assert.True(await database.StockItems.AnyAsync(item => item.Id == registered.Id && item.QuantityOnHand == 3).ConfigureAwait(false));
        }

        using HttpResponseMessage allowedAdjust = await client.PostAsJsonAsync($"/inventory/stock-items/{stockItemId}/adjust", new { Id = stockItemId, Delta = -2 }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, allowedAdjust.StatusCode);
        StockItemDto? adjusted = await allowedAdjust.Content.ReadFromJsonAsync<StockItemDto>().ConfigureAwait(false);
        Assert.NotNull(adjusted);
        Assert.Equal(6, adjusted.OnHand);

        using HttpResponseMessage allowedPolicy = await client.PutAsJsonAsync($"/inventory/stock-items/{stockItemId}/policy", new { Id = stockItemId, AllowBackorder = true, ReorderThreshold = 4 }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, allowedPolicy.StatusCode);
        StockItemDto? policyUpdated = await allowedPolicy.Content.ReadFromJsonAsync<StockItemDto>().ConfigureAwait(false);
        Assert.NotNull(policyUpdated);
        Assert.True(policyUpdated.AllowBackorder);
        Assert.Equal(4, policyUpdated.ReorderThreshold);

        await using (var database = new InventoryDbContext(databaseOptions, null!))
        {
            StockItem persisted = await database.StockItems.SingleAsync(item => item.Id == stockItemId).ConfigureAwait(false);
            Assert.Equal(6, persisted.QuantityOnHand);
            Assert.True(persisted.AllowBackorder);
            Assert.Equal(4, persisted.ReorderThreshold);
        }

        string foreignManagerToken = await keycloak.GetUserTokenAsync("integration-foreign-manager", "local-only-test-foreign-manager-password", ClientId, clientSecret: ClientSecret).ConfigureAwait(false);
        using (JsonDocument foreignPayload = ReadTokenPayload(foreignManagerToken))
        {
            Assert.Contains(keycloak.ForeignOrganizationId, foreignPayload.RootElement.GetProperty("organization")
                .EnumerateObject().Select(value => value.Value.GetProperty("id").GetString()));
        }

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", foreignManagerToken);
        using HttpResponseMessage foreignRead = await client.GetAsync($"/inventory/availability?productId={productId}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, foreignRead.StatusCode);
        AvailabilityDto? foreignAvailability = await foreignRead.Content.ReadFromJsonAsync<AvailabilityDto>().ConfigureAwait(false);
        Assert.NotNull(foreignAvailability);
        Assert.Equal(0, foreignAvailability.Available);
        using HttpResponseMessage foreignAdjust = await client.PostAsJsonAsync($"/inventory/stock-items/{stockItemId}/adjust", new { Id = stockItemId, Delta = -1 }).ConfigureAwait(false);
        Assert.NotEqual(HttpStatusCode.OK, foreignAdjust.StatusCode);
        await using (var unchangedDatabase = new InventoryDbContext(databaseOptions, null!))
        {
            StockItem unchanged = await unchangedDatabase.StockItems.SingleAsync(item => item.Id == stockItemId).ConfigureAwait(false);
            Assert.Equal(6, unchanged.QuantityOnHand);
        }

        var otherTenantOptions = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant($"{organizationId}-other")
            .Options;
        await using (var database = new InventoryDbContext(otherTenantOptions, null!))
        {
            Assert.Empty(await database.StockItems.Where(item => item.Id == stockItemId).ToListAsync().ConfigureAwait(false));
        }
    }

    public async ValueTask DisposeAsync()
    {
        client?.Dispose();
        if (factory is not null)
        {
            await factory.DisposeAsync().ConfigureAwait(false);
        }

        if (connectionString is not null)
        {
            await containers.TruncateAllTablesAsync(connectionString).ConfigureAwait(false);
        }
    }

    private void AssertOrganizationAudience(JsonElement payload)
    {
        JsonElement audience = payload.GetProperty("aud");
        Assert.True(audience.ValueKind == JsonValueKind.String
            ? audience.GetString() == "inventory-api"
            : audience.EnumerateArray().Any(value => value.GetString() == "inventory-api"));
        JsonElement memberships = payload.GetProperty("organization");
        string organizationId = memberships.EnumerateObject()
            .Select(value => value.Value.GetProperty("id").GetString())
            .Single(id => !string.IsNullOrWhiteSpace(id))!;
        Assert.Equal(keycloak.OrganizationId, organizationId);
    }

    private static JsonDocument ReadTokenPayload(string token)
    {
        string[] segments = token.Split('.');
        return JsonDocument.Parse(Base64UrlDecode(segments[1]));
    }

    private static byte[] Base64UrlDecode(string value)
    {
        string normalized = value.Replace('-', '+').Replace('_', '/');
        normalized = normalized.PadRight(normalized.Length + ((4 - (normalized.Length % 4)) % 4), '=');
        return Convert.FromBase64String(normalized);
    }

    private sealed class InventoryWebApplicationFactory(
        string databaseConnectionString,
        string rabbitMqConnectionString,
        string authority) : WebApplicationFactory<Program>
    {
        static InventoryWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:InventoryWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:InventoryRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitMqConnectionString);
            builder.UseSetting("Keycloak:realm", KeycloakTestRealmFixture.Realm);
            builder.UseSetting("Keycloak:auth-server-url", authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)]);
            builder.UseSetting("Keycloak:resource", "inventory-api");
            builder.UseSetting("Keycloak:credentials:secret", InventoryResourceClientSecret);
            builder.ConfigureTestServices(services => services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.RequireHttpsMetadata = false;
                    options.MetadataAddress = authority + "/.well-known/openid-configuration";
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        options.MetadataAddress,
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever { RequireHttps = false });
                    options.MapInboundClaims = false;
                }));
        }
    }
}
