// <copyright file="PriceResolutionTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Net.Http.Json;
using Finbuckle.MultiTenant.Extensions;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Pricing.Application.Database;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Pricing.IntegrationTests;

/// <summary>End-to-end resolve tests against a real Postgres via Testcontainers: native currency and cross-currency FX.</summary>
[Collection("SharedTestcontainers")]
public sealed class PriceResolutionTests : PricingIntegrationTestBase
{
    /// <summary>Initializes a new instance of the <see cref="PriceResolutionTests"/> class.</summary>
    /// <param name="fixture">The shared Testcontainers fixture.</param>
    public PriceResolutionTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>Resolving in the list's native currency applies the matching quantity tier.</summary>
    [Fact]
    public async Task Resolve_NativeCurrency_ReturnsTieredPrice()
    {
        var productId = Guid.NewGuid();

        var created = await Client.PostAsJsonAsync("/price-lists", new
        {
            Name = "Retail USD",
            Currency = "USD",
        });
        created.EnsureSuccessStatusCode();
        var list = await created.Content.ReadFromJsonAsync<PriceListDto>();

        var priced = await Client.PutAsJsonAsync($"/price-lists/{list!.Id}/prices/{productId}", new
        {
            Id = list.Id,
            ProductId = productId,
            Amount = 10m,
            Tiers = new[] { new { MinQuantity = 10, Amount = 8m } },
        });
        priced.EnsureSuccessStatusCode();

        var activated = await Client.PostAsJsonAsync($"/price-lists/{list.Id}/activate", new { Id = list.Id });
        activated.EnsureSuccessStatusCode();

        var resolved = await Client.GetFromJsonAsync<ResolvedPriceDto>(
            $"/prices/resolve?productId={productId}&currency=USD&quantity=10");

        Assert.NotNull(resolved);
        Assert.False(resolved!.Converted);
        Assert.Equal(8m, resolved.UnitAmount);
        Assert.Equal("USD", resolved.Currency);
    }

    /// <summary>Resolving in a different currency than the winning list applies the seeded FX rate.</summary>
    [Fact]
    public async Task Resolve_CrossCurrency_UsesSeededRate()
    {
        var productId = Guid.NewGuid();

        var created = await Client.PostAsJsonAsync("/price-lists", new { Name = "EUR list", Currency = "EUR" });
        created.EnsureSuccessStatusCode();
        var list = await created.Content.ReadFromJsonAsync<PriceListDto>();

        await (await Client.PutAsJsonAsync($"/price-lists/{list!.Id}/prices/{productId}", new
        {
            Id = list.Id,
            ProductId = productId,
            Amount = 10m,
            Tiers = Array.Empty<object>(),
        })).EnsureSuccessOrThrowAsync();
        await (await Client.PostAsJsonAsync($"/price-lists/{list.Id}/activate", new { Id = list.Id })).EnsureSuccessOrThrowAsync();

        await (await Client.PutAsJsonAsync("/exchange-rates", new
        {
            FromCurrency = "EUR",
            ToCurrency = "USD",
            Rate = 1.1m,
        })).EnsureSuccessOrThrowAsync();

        var resolved = await Client.GetFromJsonAsync<ResolvedPriceDto>(
            $"/prices/resolve?productId={productId}&currency=USD&quantity=1");

        Assert.NotNull(resolved);
        Assert.True(resolved!.Converted);
        Assert.Equal(11.00m, resolved.UnitAmount);
        Assert.Equal(1.1m, resolved.RateApplied);
    }

    [Fact]
    public async Task GetPriceList_ForeignTenantList_IsExcluded()
    {
        PriceList foreignList = PriceList.Create(
            "Foreign tenant list",
            new PriceScope("USD", country: null, customerGroupId: null, channelId: null),
            validFrom: null,
            validUntil: null,
            tenantId: "tenant-b");

        await using (var seed = new PricingDbContext(
            new DbContextOptionsBuilder<PricingDbContext>()
                .UseNpgsql(DatabaseConnectionString)
                .UseTeckCloudTenant("tenant-b")
                .Options,
            null!))
        {
            seed.PriceLists.Add(foreignList);
            await seed.SaveChangesAsync();
        }

        HttpResponseMessage response = await Client.GetAsync($"/price-lists/{foreignList.Id}");

        Assert.Equal(System.Net.HttpStatusCode.OK, response.StatusCode);
        Assert.Empty(await response.Content.ReadAsStringAsync());
    }
}

/// <summary>Boots Pricing.Host in-memory against a Testcontainers Postgres, with mock auth.</summary>
public abstract class PricingIntegrationTestBase : IDisposable
{
    private readonly SharedTestcontainersFixture fixture;
    private readonly string databaseConnectionString;
    private readonly WebApplicationFactory<Program> factory;

    /// <summary>Initializes a new instance of the <see cref="PricingIntegrationTestBase"/> class.</summary>
    /// <param name="fixture">The shared Testcontainers fixture.</param>
    protected PricingIntegrationTestBase(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;

        // Migrations live in Pricing.Host (migrationsAssembly: typeof(Program).Assembly in AddPricingPersistence).
        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(typeof(Pricing.Application.Database.PricingDbContext), "Pricing.Host")
            .GetAwaiter().GetResult();

        factory = new PricingWebApplicationFactory(databaseConnectionString);
        Client = factory.CreateClient();
    }

    /// <summary>Gets the HTTP client for the in-memory Pricing.Host.</summary>
    protected HttpClient Client { get; }

    /// <summary>Gets the PostgreSQL connection string shared by this test project.</summary>
    protected string DatabaseConnectionString => databaseConnectionString;

    /// <summary>Gets the running pricing host service provider.</summary>
    protected IServiceProvider Services => factory.Services;

    /// <inheritdoc/>
    public void Dispose()
    {
        Client.Dispose();
        factory.Dispose();
        fixture.TruncateAllTablesAsync(databaseConnectionString).GetAwaiter().GetResult();
    }

    private sealed class PricingWebApplicationFactory(string databaseConnectionString) : WebApplicationFactory<Program>
    {
        static PricingWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Run as Development so AddTeckMessaging uses Wolverine's dynamic runtime codegen
            // (Static mode would require pre-generated handler code, which tests do not produce)
            // and creates the `wolverine` message-store schema on startup (no migrate init
            // container runs in tests). See WolverinePersistenceConfigurator.ConfigureCoreRuntime.
            builder.UseEnvironment("Development");

            builder.UseSetting("ConnectionStrings:PricingWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:PricingRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "pricing-api");

            builder.ConfigureTestServices(services =>
            {
                services.AddTransient<MockBearerAuthenticationHandler>();
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    var bearer = options.Schemes.FirstOrDefault(s => s.Name == MockBearerAuthenticationHandler.SchemeName);
                    if (bearer is not null)
                    {
                        bearer.HandlerType = typeof(MockBearerAuthenticationHandler);
                    }

                    options.DefaultAuthenticateScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = MockBearerAuthenticationHandler.SchemeName;
                });

                var keycloakHandler = services.FirstOrDefault(
                    d => d.ImplementationType?.Name == "ParameterizedProtectedResourceRequirementHandler");
                if (keycloakHandler is not null)
                {
                    services.Remove(keycloakHandler);
                }

                services.AddSingleton<IAuthorizationHandler, PermissiveProtectedResourceHandler>();
            });
        }
    }

    private sealed class PermissiveProtectedResourceHandler
        : AuthorizationHandler<ParameterizedProtectedResourceRequirement>
    {
        protected override Task HandleRequirementAsync(
            AuthorizationHandlerContext context,
            ParameterizedProtectedResourceRequirement requirement)
        {
            if (context.User.Identity?.IsAuthenticated == true)
            {
                context.Succeed(requirement);
            }

            return Task.CompletedTask;
        }
    }
}

/// <summary>Small helper to surface non-success responses with their body.</summary>
internal static class HttpResponseAssertions
{
    /// <summary>Throws with the response body when the response is not a success status code.</summary>
    /// <param name="response">The HTTP response.</param>
    public static async Task EnsureSuccessOrThrowAsync(this HttpResponseMessage response)
    {
        if (!response.IsSuccessStatusCode)
        {
            string body = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"{(int)response.StatusCode}: {body}");
        }
    }
}
