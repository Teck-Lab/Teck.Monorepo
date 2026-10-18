// <copyright file="KeycloakPricingAuthorizationTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net;
using System.Text.Json;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.IdentityModel.Protocols;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Pricing.Application.Database;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Infrastructure.Database.EFCore;
using JasperFx.CommandLine;
using Teck.Platform.IntegrationTests.Shared;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Pricing.IntegrationTests;

/// <summary>Exercises Pricing.Host directly with a signed Keycloak token and verifies persisted state.</summary>
[Collection("KeycloakTestRealm")]
public sealed class KeycloakPricingAuthorizationTests : IAsyncLifetime
{
    private const string Manager = "integration-manager";
    private const string ManagerPassword = "local-only-test-manager-password";
    private const string PricingClientId = "integration-test-client";
    private const string PricingClientSecret = "local-only-test-integration-token-client-secret-not-for-production";
    private const string PricingResourceClientSecret = "local-only-test-pricing-api-secret-not-for-production";
    private readonly KeycloakTestRealmFixture keycloak;
    private readonly SharedTestcontainersFixture containers;
    private WebApplicationFactory<Program>? factory;
    private HttpClient? client;
    private string? connectionString;

    /// <summary>Initializes a new instance of the <see cref="KeycloakPricingAuthorizationTests"/> class.</summary>
    public KeycloakPricingAuthorizationTests(KeycloakTestRealmFixture keycloak, SharedTestcontainersFixture containers)
    {
        this.keycloak = keycloak;
        this.containers = containers;
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        connectionString = await containers
            .CreateSharedTestDatabaseAsync(typeof(PricingDbContext), "Pricing.Host")
            .ConfigureAwait(false);
        factory = new PricingWebApplicationFactory(connectionString, keycloak.Authority, containers.RabbitMqConnectionString);
        client = factory.CreateClient();
    }

    /// <summary>A real Keycloak manager can create a price list through Pricing.Host and round-trip it from PostgreSQL.</summary>
    private const string Reader = "integration-reader";
    private const string ReaderPassword = "local-only-test-reader-password";
    [Fact]
    public async Task CreatePriceList_WithRealKeycloakToken_AuthorizesAndPersists()
    {
        var request = new
        {
            name = $"Keycloak integration {Guid.NewGuid():N}",
            description = "Created using a real Keycloak-issued token.",
            currency = "USD",
            country = (string?)null,
            customerGroupId = (Guid?)null,
            channelId = (Guid?)null,
            validFrom = (DateTimeOffset?)null,
            validUntil = (DateTimeOffset?)null,
        };

        client!.DefaultRequestHeaders.Authorization = null;
        using HttpResponseMessage anonymousResponse = await client.PostAsJsonAsync("/price-lists", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, anonymousResponse.StatusCode);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "invalid-token");
        using HttpResponseMessage invalidResponse = await client.PostAsJsonAsync("/price-lists", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Unauthorized, invalidResponse.StatusCode);

        string token = await keycloak.GetUserTokenAsync(
            Manager,
            ManagerPassword,
            PricingClientId,
            clientSecret: PricingClientSecret).ConfigureAwait(false);
        string readerToken = await keycloak.GetUserTokenAsync(
            Reader,
            ReaderPassword,
            PricingClientId,
            clientSecret: PricingClientSecret).ConfigureAwait(false);
        using (JsonDocument tokenPayload = ReadTokenPayload(token))
        {
            Assert.Equal(keycloak.Authority, tokenPayload.RootElement.GetProperty("iss").GetString());
        }
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", readerToken);
        using HttpResponseMessage forbiddenResponse = await client.PostAsJsonAsync("/price-lists", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Forbidden, forbiddenResponse.StatusCode);

        string issuer;
        string organizationId;
        using (JsonDocument tokenPayload = ReadTokenPayload(token))
        {
            issuer = tokenPayload.RootElement.GetProperty("iss").GetString()!;
            JsonElement memberships = tokenPayload.RootElement.GetProperty("organization");
            Assert.Equal(JsonValueKind.Array, memberships.ValueKind);
            JsonElement membership = memberships.EnumerateArray().Single(value => value.ValueKind == JsonValueKind.Object);
            organizationId = membership.EnumerateObject()
                .Select(value => value.Value.TryGetProperty("id", out JsonElement id) ? id.GetString() : null)
                .Single(id => !string.IsNullOrWhiteSpace(id))!;
        }

        Assert.Equal(keycloak.Authority, issuer);
        Assert.Equal(keycloak.OrganizationId, organizationId);
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", token);
        using HttpResponseMessage createdResponse = await client.PostAsJsonAsync("/price-lists", request).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.Created, createdResponse.StatusCode);
        PriceListDto created = (await createdResponse.Content.ReadFromJsonAsync<PriceListDto>().ConfigureAwait(false))!;
        Assert.Equal(request.name, created.Name);
        using HttpResponseMessage readResponse = await client.GetAsync($"/price-lists/{created.Id}").ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, readResponse.StatusCode);
        PriceListDto? roundTripped = await readResponse.Content.ReadFromJsonAsync<PriceListDto>().ConfigureAwait(false);
        Assert.NotNull(roundTripped);
        Assert.Equal(created.Id, roundTripped.Id);
        Assert.Equal(request.name, roundTripped.Name);
        var options = new DbContextOptionsBuilder<PricingDbContext>()
            .UseNpgsql(connectionString)
            .UseTeckCloudTenant(organizationId)
            .Options;
        await using var db = new PricingDbContext(options, null!);
        var persisted = await db.PriceLists.SingleAsync(list => list.Id == created.Id).ConfigureAwait(false);
        Assert.Equal(request.name, persisted.Name);
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

    private static JsonDocument ReadTokenPayload(string token)
    {
        string[] segments = token.Split('.');
        string payload = segments[1].Replace('-', '+').Replace('_', '/');
        payload = payload.PadRight(payload.Length + ((4 - (payload.Length % 4)) % 4), '=');
        return JsonDocument.Parse(Convert.FromBase64String(payload));
    }
    private sealed class PricingWebApplicationFactory(string connectionString, string authority, string rabbitMqConnectionString) : WebApplicationFactory<Program>
    {
        static PricingWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:PricingWrite", connectionString);
            builder.UseSetting("ConnectionStrings:PricingRead", connectionString);
            builder.UseSetting("ConnectionStrings:Default", connectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitMqConnectionString);
            builder.UseSetting("Keycloak:auth-server-url", authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)]);
            builder.UseSetting("Keycloak:realm", KeycloakTestRealmFixture.Realm);
            builder.ConfigureAppConfiguration((_, configuration) => configuration.AddInMemoryCollection(
                new Dictionary<string, string?>
                {
                    ["Keycloak:auth-server-url"] = authority[..authority.LastIndexOf("/realms/", StringComparison.Ordinal)],
                    ["Keycloak:realm"] = KeycloakTestRealmFixture.Realm,
                    ["Keycloak:resource"] = "pricing-api",
                    ["Keycloak:credentials:secret"] = PricingResourceClientSecret,
                }));
            builder.ConfigureTestServices(services => services.PostConfigure<Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerOptions>(
                Microsoft.AspNetCore.Authentication.JwtBearer.JwtBearerDefaults.AuthenticationScheme,
                options =>
                {
                    options.Authority = authority;
                    options.MetadataAddress = $"{authority}/.well-known/openid-configuration";
                    options.ConfigurationManager = new ConfigurationManager<OpenIdConnectConfiguration>(
                        options.MetadataAddress,
                        new OpenIdConnectConfigurationRetriever(),
                        new HttpDocumentRetriever { RequireHttps = false });
                }));
        }
    }
}
