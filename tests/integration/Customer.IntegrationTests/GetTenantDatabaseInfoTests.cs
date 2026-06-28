using Customers.Application.Database;
using Customers.Host.Database;
using Customers.Host.Grpc.V1;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Database;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>
/// Integration tests for the <see cref="GetTenantDatabaseInfoCommandHandler"/>.
/// Verifies that the migration-seeded dev tenant is resolvable through the full repository stack.
///
/// Uses a minimal <see cref="IServiceProvider"/> (EF Core + repository + handler only) rather than
/// <see cref="Microsoft.AspNetCore.Mvc.Testing.WebApplicationFactory{TEntryPoint}"/> because
/// <c>Customer.Host</c> has no HTTP endpoints and <c>AddTeckService</c> calls
/// <c>AddFastEndpoints</c> which throws when zero endpoint declarations are found.
/// </summary>
[Collection("SharedTestcontainers")]
public sealed class GetTenantDatabaseInfoTests : IAsyncDisposable
{
    /// <summary>The GUID of the dev tenant seeded by the InitialCustomer migration.</summary>
    private static readonly Guid DevTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private readonly ServiceProvider serviceProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTenantDatabaseInfoTests"/> class.
    /// Applies the customer migrations against a testcontainer Postgres database and wires a minimal
    /// service provider with just the EF Core read context, repository, and command handler.
    /// </summary>
    /// <param name="fixture">The shared testcontainers fixture providing Postgres.</param>
    public GetTenantDatabaseInfoTests(SharedTestcontainersFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);

        // Run migrations (including the dev-tenant seed) in the shared test database.
        // The migrations assembly is Customer.Host (typeof(Program).Assembly.GetName().Name).
        // We do NOT truncate after the test because the seeded data comes from HasData in the
        // migration; truncation would remove it and migrations would not re-run on the next
        // test run since the database already exists.
        string connectionString = fixture
            .CreateSharedTestDatabaseAsync(typeof(CustomerDbContext), "Customer.Host")
            .GetAwaiter()
            .GetResult();

        // Build a minimal DI container — just what the handler needs.
        // We skip the full host startup (WebApplicationFactory) because Customer.Host has no
        // HTTP endpoints and FastEndpoints throws when zero endpoint declarations are found.
        var services = new ServiceCollection();

        services.AddLogging();
        services.AddHttpContextAccessor();

        // Stub IMultiTenantContextAccessor<TenantDetails>: needed by CustomerReadDbContext
        // constructor. Customer.Host is the global tenant authority and Tenant is NOT
        // tenant-scoped, so a null MultiTenantContext (= no tenant filter) is correct here.
        services.AddSingleton<IMultiTenantContextAccessor<TenantDetails>>(
            new NullMultiTenantContextAccessor());

        services.AddDbContext<CustomerReadDbContext>((_, opts) =>
        {
            opts.UseNpgsql(connectionString);
            opts.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        services.AddScoped(typeof(IGenericReadRepository<,>), typeof(CustomerReadRepository<,>));
        services.AddScoped<GetTenantDatabaseInfoCommandHandler>();

        serviceProvider = services.BuildServiceProvider();
    }

    /// <summary>
    /// Verifies that the dev tenant seeded by the InitialCustomer migration can be resolved
    /// by the <see cref="GetTenantDatabaseInfoCommandHandler"/> using the full repository stack.
    /// </summary>
    [Fact]
    public async Task RemoteHandler_ResolvesSeededDevTenant()
    {
        // Arrange
        await using AsyncServiceScope scope = serviceProvider.CreateAsyncScope();
        var handler = scope.ServiceProvider.GetRequiredService<GetTenantDatabaseInfoCommandHandler>();
        var command = new GetTenantDatabaseInfoCommand
        {
            TenantId = DevTenantId.ToString(),
            ServiceName = "integration-test",
        };

        // Act
        TenantDatabaseInfoRpcResult result = await handler.ExecuteAsync(command, CancellationToken.None);

        // Assert
        Assert.True(result.Found, $"Dev tenant '{DevTenantId}' was not found. ErrorDetail: {result.ErrorDetail}");
        Assert.Equal("shared", result.DatabaseStrategy);
        Assert.Equal("postgres", result.DatabaseProvider);
        Assert.Equal("dev", result.Identifier);
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        await serviceProvider.DisposeAsync();
    }

    /// <summary>
    /// Stub <see cref="IMultiTenantContextAccessor{TTenantInfo}"/> with no active tenant context.
    /// Used in tests where the repository must be accessible without tenant isolation
    /// (the Customer service itself, whose <c>Tenant</c> aggregate is the global authority).
    /// </summary>
    private sealed class NullMultiTenantContextAccessor
        : IMultiTenantContextAccessor<TenantDetails>,
          IMultiTenantContextAccessor
    {
        /// <inheritdoc/>
        public IMultiTenantContext<TenantDetails>? MultiTenantContext => null;

        /// <inheritdoc/>
        IMultiTenantContext? IMultiTenantContextAccessor.MultiTenantContext => null;
    }
}
