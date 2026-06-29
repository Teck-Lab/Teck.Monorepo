using System.Net;
using System.Net.Http.Json;
using Finbuckle.MultiTenant.Extensions;
using JasperFx.CommandLine;
using Keycloak.AuthServices.Authorization.Requirements;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Orders.Application.Orders.Responses;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Orders.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class CreateOrderTests : OrderIntegrationTestBase
{
    public CreateOrderTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    [Fact]
    public async Task PostOrders_WithValidBody_ReturnsCreatedOrder()
    {
        var response = await Client.PostAsJsonAsync(
            "/orders",
            new
            {
                customerId = Guid.NewGuid(),
                lines = new[]
                {
                    new
                    {
                        productId = Guid.NewGuid(),
                        productName = "Test Product",
                        quantity = 2,
                        unitPrice = 19.95m,
                    },
                },
            });

        Assert.Equal(HttpStatusCode.Created, response.StatusCode);

        var order = await response.Content.ReadFromJsonAsync<OrderDto>();

        Assert.NotNull(order);
        Assert.NotEqual(Guid.Empty, order!.Id);
        Assert.Equal(1, order.Lines.Count);
        Assert.NotNull(response.Headers.Location);
        Assert.Equal($"/orders/{order.Id}", response.Headers.Location!.OriginalString);
    }

    [Fact]
    public async Task PostOrders_WithEmptyLines_ReturnsBadRequest()
    {
        var response = await Client.PostAsJsonAsync(
            "/orders",
            new
            {
                customerId = Guid.NewGuid(),
                lines = Array.Empty<object>(),
            });

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task GetOrders_AfterCreation_ReturnsCreatedOrder()
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();

        var createResponse = await Client.PostAsJsonAsync(
            "/orders",
            new
            {
                customerId,
                lines = new[]
                {
                    new
                    {
                        productId,
                        productName = "Test Product",
                        quantity = 3,
                        unitPrice = 12.5m,
                    },
                },
            });

        createResponse.EnsureSuccessStatusCode();

        var createdOrder = await createResponse.Content.ReadFromJsonAsync<OrderDto>();

        Assert.NotNull(createdOrder);

        var getResponse = await Client.GetAsync($"/orders/{createdOrder!.Id}");

        Assert.Equal(HttpStatusCode.OK, getResponse.StatusCode);

        var order = await getResponse.Content.ReadFromJsonAsync<OrderDto>();

        Assert.NotNull(order);
        Assert.Equal(createdOrder.Id, order!.Id);
        Assert.Equal(createdOrder.CustomerId, order.CustomerId);
        Assert.Equal(createdOrder.Total, order.Total);
        Assert.Single(order.Lines);
        Assert.Equal(productId, order.Lines[0].ProductId);
    }
}

public abstract class OrderIntegrationTestBase : IDisposable
{
    private readonly SharedTestcontainersFixture fixture;
    private readonly string databaseConnectionString;
    private readonly WebApplicationFactory<Program> factory;

    protected OrderIntegrationTestBase(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;

        // Migrations live in Order.Host (migrationsAssembly: typeof(Program).Assembly in AddOrderPersistence).
        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(
                typeof(Orders.Application.Database.OrderDbContext),
                "Order.Host")
            .GetAwaiter()
            .GetResult();

        factory = new OrderWebApplicationFactory(fixture, databaseConnectionString);
        Client = factory.CreateClient();
    }

    protected HttpClient Client { get; }

    public void Dispose()
    {
        Client.Dispose();
        factory.Dispose();
        fixture.TruncateAllTablesAsync(databaseConnectionString).GetAwaiter().GetResult();
    }

    private sealed class OrderWebApplicationFactory(
        SharedTestcontainersFixture fixture,
        string databaseConnectionString) : WebApplicationFactory<Program>
    {
        static OrderWebApplicationFactory()
        {
            // Order.Host/Program.cs runs the host via RunJasperFxCommands so the `codegen write`
            // command works in container builds. When WebApplicationFactory invokes that entry point
            // with no command, the JasperFx command runner would return an exit code instead of
            // starting the in-memory server. AutoStartHost tells JasperFx to start the host normally
            // in that case, which is exactly what WebApplicationFactory needs.
            JasperFxEnvironment.AutoStartHost = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // UseSetting applies at the highest configuration priority and overrides appsettings
            // connection strings that AddOrderPersistence reads during Program.cs setup.
            builder.UseSetting("ConnectionStrings:OrderWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:OrderRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            // Minimal Keycloak stubs so the production binding of KeycloakAuthenticationOptions
            // does not throw at startup. Actual JWT validation is replaced by MockBearerAuthenticationHandler.
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "order-api");

            builder.ConfigureTestServices(services =>
            {
                // Register Finbuckle multi-tenant infrastructure so IMultiTenantContextAccessor<TenantDetails>
                // is available. No strategy or store is configured, so MultiTenantContext will be null per
                // request and the DbContext factories will fall back to the default connection string.
                services.AddMultiTenant<TenantDetails>();

                // Handler discovery for the Order.Application assembly is configured in
                // Order.Host/Program.cs (opts.Discovery.IncludeAssembly), so it applies here too —
                // the test boots the real host via WebApplicationFactory and needs no test-only
                // discovery wiring.

                // Replace the Keycloak JWT bearer handler with the test-only mock so that
                // AuthSchemes(JwtBearerDefaults.AuthenticationScheme) in AuthenticatedEndpoint
                // resolves this handler instead of attempting real JWT validation.
                //
                // AddKeycloak in Order.Host/Program.cs already registers "Bearer" as JwtBearerHandler.
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
                // protected resource requirements. Remove it and replace with a permissive test handler
                // that succeeds the requirement for any authenticated user without network calls.
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
