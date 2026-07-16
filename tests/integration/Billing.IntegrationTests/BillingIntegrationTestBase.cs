// <copyright file="BillingIntegrationTestBase.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Billings.Application.Database;
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

namespace Billing.IntegrationTests;

/// <summary>
/// Shared fixture base for billing integration tests: boots Billing.Host in-memory
/// via <see cref="WebApplicationFactory{TEntryPoint}"/> against a Testcontainers-backed Postgres
/// database, and replaces Keycloak JWT auth with a mock handler that always authenticates the
/// request with a synthetic tenant claim.
/// </summary>
public abstract class BillingIntegrationTestBase : IDisposable
{
    private readonly SharedTestcontainersFixture fixture;
    private readonly string databaseConnectionString;
    private readonly WebApplicationFactory<Program> factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="BillingIntegrationTestBase"/> class.
    /// </summary>
    /// <param name="fixture">The shared testcontainers fixture providing Postgres.</param>
    protected BillingIntegrationTestBase(SharedTestcontainersFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;

        // Migrations live in Billing.Host (migrationsAssembly: typeof(Program).Assembly in AddBillingPersistence).
        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(
                typeof(BillingDbContext),
                "Billing.Host")
            .GetAwaiter()
            .GetResult();

        factory = new BillingWebApplicationFactory(fixture, databaseConnectionString);
        Client = factory.CreateClient();
    }

    /// <summary>Gets the HTTP client used to exercise the in-memory host.</summary>
    protected HttpClient Client { get; }

    /// <summary>
    /// Gets the host's root service provider, exposed so subclasses can open independent
    /// <see cref="IServiceScope"/>s if needed.
    /// </summary>
    protected IServiceProvider Services => factory.Services;

    /// <inheritdoc/>
    public void Dispose()
    {
        Client.Dispose();
        factory.Dispose();
        fixture.TruncateAllTablesAsync(databaseConnectionString).GetAwaiter().GetResult();
        GC.SuppressFinalize(this);
    }

    private sealed class BillingWebApplicationFactory(
        SharedTestcontainersFixture fixture,
        string databaseConnectionString) : WebApplicationFactory<Program>
    {
        static BillingWebApplicationFactory()
        {
            // Billing.Host/Program.cs runs the host via RunTeckServiceAsync, which wraps JasperFx
            // command execution so the `codegen write` command works in container builds. When
            // WebApplicationFactory invokes that entry point with no command, the JasperFx command
            // runner would return an exit code instead of starting the in-memory server.
            // AutoStartHost tells JasperFx to start the host normally in that case, which is
            // exactly what WebApplicationFactory needs.
            JasperFxEnvironment.AutoStartHost = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // UseSetting applies at the highest configuration priority and overrides appsettings
            // connection strings that AddBillingPersistence reads during Program.cs setup.
            builder.UseSetting("ConnectionStrings:BillingWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:BillingRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            // Minimal Keycloak stubs so the production binding of KeycloakAuthenticationOptions
            // does not throw at startup. Actual JWT validation is replaced by MockBearerAuthenticationHandler.
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "billing-api");

            builder.ConfigureTestServices(services =>
            {
                // Register Finbuckle multi-tenant infrastructure so IMultiTenantContextAccessor<TenantDetails>
                // is available. No strategy or store is configured, so MultiTenantContext will be null per
                // request and the DbContext factories will fall back to the default connection string.
                services.AddMultiTenant<TenantDetails>();

                // Handler discovery for the Billing.Application assembly is configured in
                // Billing.Host/Program.cs (opts.Discovery.IncludeAssembly), so it applies here too —
                // the test boots the real host via WebApplicationFactory and needs no test-only
                // discovery wiring.

                // Replace the Keycloak JWT bearer handler with the test-only mock so that
                // AuthSchemes(JwtBearerDefaults.AuthenticationScheme) in AuthenticatedEndpoint
                // (used by CapturePaymentEndpoint/GetPaymentEndpoint/ListPaymentsEndpoint/
                // GetInvoiceEndpoint, none of which are anonymous) resolves this handler instead of
                // attempting real JWT validation.
                //
                // AddKeycloak in Billing.Host/Program.cs already registers "Bearer" as JwtBearerHandler.
                // Calling AddScheme("Bearer") again would throw "Scheme already exists: Bearer".
                // Instead, use PostConfigure<AuthenticationOptions> to replace the HandlerType of
                // the existing "Bearer" scheme builder and register our mock handler in DI.
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

                // Keycloak.AuthServices registers ParameterizedProtectedResourceRequirementHandler which
                // makes HTTP calls to the Keycloak authorization server (localhost:8080) to evaluate
                // protected resource requirements (required by every billing endpoint, none of which
                // are anonymous). Remove it and replace with a permissive test handler that succeeds
                // the requirement for any authenticated user without network calls.
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

    // Test-only authorization handler that bypasses Keycloak's ProtectedResourceRequirement
    // for any authenticated user. Registered only via ConfigureTestServices — never in production.
    private sealed class PermissiveProtectedResourceHandler
        : AuthorizationHandler<ParameterizedProtectedResourceRequirement>
    {
        /// <inheritdoc/>
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
