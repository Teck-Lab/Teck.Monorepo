// <copyright file="CatalogIntegrationTestBase.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Finbuckle.MultiTenant.Extensions;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;

namespace Catalog.IntegrationTests;

/// <summary>
/// Boots <c>Catalog.Host</c> in-memory via <see cref="WebApplicationFactory{TEntryPoint}"/> against a
/// Testcontainers-backed Postgres database, replacing Keycloak JWT auth with a mock handler that always
/// authenticates the request and a permissive protected-resource handler.
/// </summary>
public abstract class CatalogIntegrationTestBase : IDisposable
{
    private readonly SharedTestcontainersFixture fixture;
    private readonly string databaseConnectionString;
    private readonly WebApplicationFactory<Program> factory;

    /// <summary>Initializes a new instance of the <see cref="CatalogIntegrationTestBase"/> class.</summary>
    /// <param name="fixture">The shared Testcontainers fixture.</param>
    protected CatalogIntegrationTestBase(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;

        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(
                typeof(Catalog.Application.Database.CatalogDbContext),
                "Catalog.Host")
            .GetAwaiter()
            .GetResult();

        factory = new CatalogWebApplicationFactory(databaseConnectionString);
        Client = factory.CreateClient();
    }

    /// <summary>Gets the HTTP client bound to the in-memory Catalog host.</summary>
    protected HttpClient Client { get; }

    /// <summary>Gets the PostgreSQL connection string shared by this test project.</summary>
    protected string DatabaseConnectionString => databaseConnectionString;

    /// <inheritdoc/>
    public void Dispose()
    {
        Client.Dispose();
        factory.Dispose();
        fixture.TruncateAllTablesAsync(databaseConnectionString).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private sealed class CatalogWebApplicationFactory(string databaseConnectionString)
        : WebApplicationFactory<Program>
    {
        static CatalogWebApplicationFactory() => JasperFxEnvironment.AutoStartHost = true;

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Run as Development so AddTeckMessaging uses Wolverine's dynamic runtime codegen
            // (Static mode would require pre-generated handler code, which tests do not produce)
            // and creates the `wolverine` message-store schema on startup (no migrate init
            // container runs in tests). See WolverinePersistenceConfigurator.ConfigureCoreRuntime.
            builder.UseEnvironment("Development");

            builder.UseSetting("ConnectionStrings:CatalogWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:CatalogRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "catalog-api");

            builder.ConfigureTestServices(services =>
            {
                services.AddTransient<MockBearerAuthenticationHandler>();
                services.PostConfigure<AuthenticationOptions>(options =>
                {
                    var bearerScheme = options.Schemes
                        .FirstOrDefault(s => s.Name == MockBearerAuthenticationHandler.SchemeName);
                    if (bearerScheme is not null)
                    {
                        bearerScheme.HandlerType = typeof(MockBearerAuthenticationHandler);
                    }

                    options.DefaultAuthenticateScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultChallengeScheme = MockBearerAuthenticationHandler.SchemeName;
                    options.DefaultForbidScheme = MockBearerAuthenticationHandler.SchemeName;
                });

                var keycloakHandlerDescriptor = services.FirstOrDefault(
                    d => d.ImplementationType?.Name == "ParameterizedProtectedResourceRequirementHandler");
                if (keycloakHandlerDescriptor is not null)
                {
                    services.Remove(keycloakHandlerDescriptor);
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
