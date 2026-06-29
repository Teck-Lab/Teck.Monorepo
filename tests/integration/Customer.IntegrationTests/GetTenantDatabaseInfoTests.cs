// <copyright file="GetTenantDatabaseInfoTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Customers.Application.Database;
using Customers.Host.Grpc.V1;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Npgsql;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>
/// Integration tests for the <see cref="GetTenantDatabaseInfoCommandHandler"/>.
/// Verifies that the dev tenant is resolvable through the full host stack.
///
/// Boots <c>Customer.Host</c> via <see cref="WebApplicationFactory{TEntryPoint}"/> against a
/// Testcontainers PostgreSQL database so the test exercises the full DI and EF pipeline.
/// The dev tenant is seeded explicitly in <see cref="InitializeAsync"/> — the migration no
/// longer seeds it so that production environments remain clean.
/// </summary>
[Collection("SharedTestcontainers")]
public sealed class GetTenantDatabaseInfoTests : IAsyncLifetime
{
    /// <summary>The GUID of the dev tenant seeded by the InitialCustomer migration.</summary>
    private static readonly Guid DevTenantId = Guid.Parse("00000000-0000-0000-0000-0000000000a1");

    private readonly SharedTestcontainersFixture fixture;
    private WebApplicationFactory<Program>? factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="GetTenantDatabaseInfoTests"/> class.
    /// </summary>
    /// <param name="fixture">The shared testcontainers fixture providing Postgres.</param>
    public GetTenantDatabaseInfoTests(SharedTestcontainersFixture fixture)
    {
        ArgumentNullException.ThrowIfNull(fixture);
        this.fixture = fixture;
    }

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        // Run EF migrations against the shared test database. The migration no longer seeds
        // the dev tenant (removed to prevent it running in production), so we seed it here
        // explicitly for the test to be self-contained.
        await fixture.CreateSharedTestDatabaseAsync(typeof(CustomerDbContext), "Customer.Host");

        string connectionString = fixture.GetDatabaseConnectionString("testdb_customerdbcontext");
        await SeedDevTenantAsync(connectionString);

        // Build the factory and trigger host startup so errors surface here rather than
        // inside an individual test method.
        factory = new CustomerWebApplicationFactory(fixture);
        _ = factory.Services;
    }

    private static async Task SeedDevTenantAsync(string connectionString)
    {
        await using var conn = new NpgsqlConnection(connectionString);
        await conn.OpenAsync();
        await using var cmd = conn.CreateCommand();
        cmd.CommandText = @"
            INSERT INTO tenants
                (""Id"", ""Identifier"", ""DatabaseStrategy"", ""DatabaseProvider"",
                 ""HasReadReplicas"", ""Status"", ""CreatedAt"", ""IsDeleted"")
            VALUES
                ('00000000-0000-0000-0000-0000000000a1', 'dev', 'shared', 'postgres',
                 false, 'active', '2026-01-01 00:00:00+00', false)
            ON CONFLICT (""Id"") DO NOTHING";
        await cmd.ExecuteNonQueryAsync();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        if (factory is not null)
        {
            await factory.DisposeAsync();
        }
    }

    /// <summary>
    /// Verifies that the dev tenant (seeded explicitly in test setup) can be resolved
    /// by the <see cref="GetTenantDatabaseInfoCommandHandler"/> using the full repository stack.
    /// </summary>
    [Fact]
    public async Task RemoteHandler_ResolvesSeededDevTenant()
    {
        // Arrange
        await using AsyncServiceScope scope = factory!.Services.CreateAsyncScope();
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

    /// <summary>
    /// <see cref="WebApplicationFactory{TEntryPoint}"/> for Customer.Host that injects the
    /// Testcontainers Postgres connection string into all three connection-string slots so that
    /// <c>AddCustomerPersistence</c> can resolve the <c>CustomerWrite</c> / <c>Default</c>
    /// connection strings without throwing.
    /// </summary>
    private sealed class CustomerWebApplicationFactory(SharedTestcontainersFixture fixture)
        : WebApplicationFactory<Program>
    {
        static CustomerWebApplicationFactory()
        {
            // Customer.Host/Program.cs runs the host via RunJasperFxCommands so the `codegen write`
            // command works in container builds. AutoStartHost tells JasperFx to start the in-memory
            // host (rather than returning a command exit code) when WebApplicationFactory invokes the
            // entry point with no command.
            JasperFxEnvironment.AutoStartHost = true;
        }

        /// <inheritdoc/>
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // The database was already created and migrated in InitializeAsync before
            // ConfigureWebHost is invoked, so the connection string is safe to use here.
            string connectionString = fixture.GetDatabaseConnectionString("testdb_customerdbcontext");

            // UseSetting applies values at the host level with the highest configuration
            // priority, ensuring they override the Dev appsettings connection strings.
            builder.UseSetting("ConnectionStrings:CustomerWrite", connectionString);
            builder.UseSetting("ConnectionStrings:CustomerRead", connectionString);
            builder.UseSetting("ConnectionStrings:Default", connectionString);

            // FastEndpoints MapHandlers wires the handler into the remote-command pipeline but
            // does not add it to the DI container as a resolvable concrete type. Register it
            // explicitly so the test can resolve it from a scope.
            builder.ConfigureServices(static services =>
                services.AddScoped<GetTenantDatabaseInfoCommandHandler>());
        }
    }
}
