// <copyright file="KeycloakCatalogAuthorizationTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Catalog.Application.Products.Responses;
using Catalog.Application.Suppliers.Responses;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Catalog.IntegrationTests;

/// <summary>Exercises direct Catalog.Host permissions using real Keycloak-issued tokens.</summary>
[Collection("KeycloakTestRealm")]
public sealed class KeycloakCatalogAuthorizationTests(
    KeycloakTestRealmFixture keycloak,
    SharedTestcontainersFixture containers) : IAsyncLifetime
{
    private const string TokenClientId = "integration-test-client";
    private const string TokenClientSecret = "local-only-test-integration-token-client-secret-not-for-production";
    private const string CatalogClientId = "catalog-api";
    private const string CatalogClientSecret = "local-only-test-catalog-api-secret-not-for-production";
    private const string Reader = "integration-reader";
    private const string ReaderPassword = "local-only-test-reader-password";
    private const string Manager = "integration-manager";
    private const string ManagerPassword = "local-only-test-manager-password";
    private string? connectionString;
    private CatalogWebApplicationFactory? factory;
    private HttpClient? client;

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        connectionString = await containers.CreateSharedTestDatabaseAsync(typeof(Catalog.Application.Database.CatalogDbContext), "Catalog.Host").ConfigureAwait(false);
        factory = new CatalogWebApplicationFactory(connectionString, containers.RabbitMqConnectionString, keycloak.Authority);
        client = factory.CreateClient();
    }

    [Fact]
    public async Task ProductAndSupplierAuthorization_WithRealKeycloak_EnforcesPermissionsAndPersistsManagerChanges()
    {
        string readerToken = await keycloak.GetUserTokenAsync(Reader, ReaderPassword, TokenClientId, clientSecret: TokenClientSecret).ConfigureAwait(false);
        string managerToken = await keycloak.GetUserTokenAsync(Manager, ManagerPassword, TokenClientId, clientSecret: TokenClientSecret).ConfigureAwait(false);
        using JsonDocument readerPayload = ReadTokenPayload(readerToken);
        JsonElement audience = readerPayload.RootElement.GetProperty("aud");
        Assert.True(audience.ValueKind == JsonValueKind.String
            ? audience.GetString() == CatalogClientId
            : audience.EnumerateArray().Any(value => value.GetString() == CatalogClientId));
        Assert.Equal(keycloak.OrganizationId, ReadOrganizationId(readerPayload.RootElement));

        client!.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        using HttpResponseMessage seedProductResponse = await client.PostAsJsonAsync("/products", ProductRequest("catalog-sensitive-" + Guid.NewGuid().ToString("N"))).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, seedProductResponse.StatusCode);
        ProductDto? seedProduct = await seedProductResponse.Content.ReadFromJsonAsync<ProductDto>().ConfigureAwait(false);
        Assert.NotNull(seedProduct);
        using HttpResponseMessage seedSupplierResponse = await client.PostAsJsonAsync("/suppliers", new { Name = "Sensitive supplier " + Guid.NewGuid().ToString("N"), ContactEmail = (string?)null, ContactPhone = (string?)null }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, seedSupplierResponse.StatusCode);
        SupplierDto? seedSupplier = await seedSupplierResponse.Content.ReadFromJsonAsync<SupplierDto>().ConfigureAwait(false);
        Assert.NotNull(seedSupplier);
        Guid variantId = seedProduct!.Variants[0].Id;
        using HttpResponseMessage seedLinkResponse = await client.PostAsJsonAsync($"/variants/{variantId}/suppliers", new
        {
            VariantId = variantId, SupplierId = seedSupplier!.Id, CostAmount = 4m, CostCurrency = "USD",
            SupplierSku = "KEYCLOAK-SUPPLIER-1", LeadTimeDays = 5, MinOrderQuantity = 1, IsPreferred = true,
        }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, seedLinkResponse.StatusCode);

        client!.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage missingToken = await client.GetAsync("/products").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, missingToken.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using HttpResponseMessage invalidToken = await client.GetAsync("/products").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidToken.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerToken);
        using HttpResponseMessage readerRead = await client.GetAsync("/products").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, readerRead.StatusCode);
        using HttpResponseMessage readerSensitiveRead = await client.GetAsync($"/variants/{variantId}/suppliers/{seedSupplier!.Id}/history").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, readerSensitiveRead.StatusCode);
        using HttpResponseMessage readerSupplierManage = await client.PostAsJsonAsync("/suppliers", new { Name = "Reader supplier", ContactEmail = (string?)null, ContactPhone = (string?)null }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, readerSupplierManage.StatusCode);
        using HttpResponseMessage readerManage = await client.PostAsJsonAsync("/products", ProductRequest("catalog-reader-denied")).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, readerManage.StatusCode);

        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", managerToken);
        using HttpResponseMessage managerSensitiveRead = await client.GetAsync($"/variants/{variantId}/suppliers/{seedSupplier!.Id}/history").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, managerSensitiveRead.StatusCode);
        Catalog.Application.Suppliers.Responses.SupplierPriceHistoryDto[]? history = await managerSensitiveRead.Content.ReadFromJsonAsync<Catalog.Application.Suppliers.Responses.SupplierPriceHistoryDto[]>().ConfigureAwait(false);
        Assert.NotNull(history);
        Assert.Contains(history!, item => item.CostPriceAmount == 4m && item.CostPriceCurrency == "USD");
        using HttpResponseMessage updateCost = await client.PutAsJsonAsync($"/variants/{variantId}/suppliers/{seedSupplier.Id}/cost", new
        {
            VariantId = variantId, SupplierId = seedSupplier.Id, CostAmount = 7m, CostCurrency = "USD",
        }).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, updateCost.StatusCode);
        using HttpResponseMessage updatedHistory = await client.GetAsync($"/variants/{variantId}/suppliers/{seedSupplier.Id}/history").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, updatedHistory.StatusCode);
        SupplierPriceHistoryDto[]? persistedHistory = await updatedHistory.Content.ReadFromJsonAsync<SupplierPriceHistoryDto[]>().ConfigureAwait(false);
        Assert.NotNull(persistedHistory);
        Assert.Contains(persistedHistory, item => item.CostPriceAmount == 4m && item.CostPriceCurrency == "USD");
        Assert.Contains(persistedHistory, item => item.CostPriceAmount == 7m && item.CostPriceCurrency == "USD");
    }

    /// <inheritdoc />
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

    private static object ProductRequest(string sku) => new
    {
        Name = "Keycloak catalog product", Description = (string?)null, CategoryId = (Guid?)null,
        Sku = sku, SellPriceAmount = 12m, SellPriceCurrency = "USD",
    };

    private static JsonDocument ReadTokenPayload(string token)
    {
        string payload = token.Split('.')[1].Replace('-', '+').Replace('_', '/');
        return JsonDocument.Parse(Convert.FromBase64String(payload.PadRight(payload.Length + ((4 - payload.Length % 4) % 4), '=')));
    }

    private static string ReadOrganizationId(JsonElement payload)
    {
        JsonElement memberships = payload.GetProperty("organization");
        return memberships.EnumerateObject()
            .Select(value => value.Value.GetProperty("id").GetString())
            .Single(id => !string.IsNullOrWhiteSpace(id))!;
    }

    private sealed class CatalogWebApplicationFactory(string connectionString, string rabbitMqConnectionString, string authority) : WebApplicationFactory<Program>
    {
        static CatalogWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:CatalogWrite", connectionString);
            builder.UseSetting("ConnectionStrings:CatalogRead", connectionString);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitMqConnectionString);
            builder.UseSetting("Keycloak:realm", KeycloakTestRealmFixture.Realm);
            builder.UseSetting("Keycloak:auth-server-url", authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)]);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:auth-server-url"] = authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)],
                ["Keycloak:realm"] = KeycloakTestRealmFixture.Realm,
                ["Keycloak:resource"] = CatalogClientId,
                ["Keycloak:credentials:secret"] = CatalogClientSecret,
            }));
            builder.ConfigureTestServices(services => services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.Authority = authority;
                    options.MetadataAddress = $"{authority}/.well-known/openid-configuration";
                    options.ConfigurationManager = new Microsoft.IdentityModel.Protocols.ConfigurationManager<Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfiguration>(
                        options.MetadataAddress,
                        new Microsoft.IdentityModel.Protocols.OpenIdConnect.OpenIdConnectConfigurationRetriever(),
                        new Microsoft.IdentityModel.Protocols.HttpDocumentRetriever { RequireHttps = false });
                    options.RequireHttpsMetadata = false;
                    options.Audience = CatalogClientId;
                    options.TokenValidationParameters.ValidAudience = CatalogClientId;
                }));
        }
    }
}
