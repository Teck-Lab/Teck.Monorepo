// <copyright file="MessageStoreSchemaTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Data;
using System.Data.Common;
using Finbuckle.MultiTenant.Extensions;
using Inventories.Application.Database;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Inventories.IntegrationTests;

/// <summary>
/// Verifies that the Wolverine <c>wolverine</c> message-store schema (outbox/inbox tables) is
/// created at host startup by <see cref="WolverinePersistenceConfigurator"/>'s unconditional
/// <c>AutoBuildMessageStorageOnStartup = AutoCreate.CreateOrUpdate</c> — i.e. WITHOUT relying on
/// the <c>--migrate</c> init container (which does not create it) and WITHOUT the former
/// Development-only gate. Boots the already-wired inventory host on the standard (RabbitMQ-backed)
/// runtime path against a fresh Testcontainers Postgres, then asserts the schema's core envelope
/// tables physically exist by querying <c>information_schema.tables</c>.
/// </summary>
/// <remarks>
/// Limitation: this boots under <c>UseEnvironment("Development")</c>, which is required for
/// Wolverine's <c>TypeLoadMode.Dynamic</c> codegen (a true Production boot loads Static
/// pre-generated handler types produced by the container build's <c>WolverineCodegenWrite</c> step,
/// which are absent in a test run). The Development environment therefore governs codegen only; it
/// no longer governs message-store creation, which is now unconditional. Proving the schema is built
/// in a literal Production environment is not possible in-process here — that guarantee rests on the
/// message-store setting no longer branching on the environment (see the configurator) plus this
/// test proving the create-or-update mechanism actually materializes the tables.
/// </remarks>
[Collection("SharedTestcontainers")]
public sealed class MessageStoreSchemaTests : IDisposable
{
    private const string WolverineSchema = "wolverine";

    private readonly SharedTestcontainersFixture fixture;
    private readonly string databaseConnectionString;
    private readonly WebApplicationFactory<Program> factory;

    /// <summary>
    /// Initializes a new instance of the <see cref="MessageStoreSchemaTests"/> class, booting the
    /// inventory host with a <c>rabbitmq</c> connection string so the standard broker-backed runtime
    /// (the production path) is exercised.
    /// </summary>
    /// <param name="fixture">The shared Testcontainers fixture (one Postgres + one RabbitMQ).</param>
    public MessageStoreSchemaTests(SharedTestcontainersFixture fixture)
    {
        this.fixture = fixture;

        databaseConnectionString = fixture
            .CreateSharedTestDatabaseAsync(typeof(InventoryDbContext), "Inventory.Host")
            .GetAwaiter()
            .GetResult();

        factory = new MessageStoreWebApplicationFactory(databaseConnectionString, fixture.RabbitMqConnectionString);
    }

    /// <summary>
    /// Asserts that after the host starts, the <c>wolverine</c> schema contains the incoming and
    /// outgoing envelope tables — proving the message store was built on startup by the
    /// unconditional create-or-update setting rather than by any migration step.
    /// </summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Host_OnStartup_CreatesWolverineMessageStoreSchema()
    {
        // Forces WebApplicationFactory to build and START the host, which runs Wolverine's startup
        // and (via AutoBuildMessageStorageOnStartup) creates the `wolverine` schema.
        using var client = factory.CreateClient();

        using var scope = factory.Services.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();

        var wolverineTables = await QueryWolverineTablesAsync(dbContext);

        Assert.Contains("wolverine_incoming_envelopes", wolverineTables);
        Assert.Contains("wolverine_outgoing_envelopes", wolverineTables);
    }

    /// <inheritdoc/>
    public void Dispose()
    {
        factory.Dispose();
        fixture.TruncateAllTablesAsync(databaseConnectionString).GetAwaiter().GetResult();
    }

    private static async Task<IReadOnlyList<string>> QueryWolverineTablesAsync(DbContext dbContext)
    {
        DbConnection connection = dbContext.Database.GetDbConnection();
        bool openedHere = false;
        try
        {
            if (connection.State != ConnectionState.Open)
            {
                await connection.OpenAsync();
                openedHere = true;
            }

            await using DbCommand command = connection.CreateCommand();
            command.CommandText =
                "SELECT table_name FROM information_schema.tables WHERE table_schema = @schema";
            DbParameter schemaParameter = command.CreateParameter();
            schemaParameter.ParameterName = "schema";
            schemaParameter.Value = WolverineSchema;
            command.Parameters.Add(schemaParameter);

            var tables = new List<string>();
            await using DbDataReader reader = await command.ExecuteReaderAsync();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            return tables;
        }
        finally
        {
            if (openedHere && connection.State == ConnectionState.Open)
            {
                await connection.CloseAsync();
            }
        }
    }

    private sealed class MessageStoreWebApplicationFactory(
        string databaseConnectionString,
        string rabbitConnectionString) : WebApplicationFactory<Program>
    {
        static MessageStoreWebApplicationFactory()
        {
            // RunTeckServiceAsync wraps JasperFx command execution; without AutoStartHost the command
            // runner returns an exit code instead of starting the in-memory server. See the same
            // static ctor on InventoryStockTests' factory.
            JasperFxEnvironment.AutoStartHost = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            // Development enables Wolverine's dynamic codegen only (tests do not pre-generate Static
            // handler types). Message-store creation is NOT gated on this — it is unconditional in
            // WolverinePersistenceConfigurator.ConfigureCoreRuntime.
            builder.UseEnvironment("Development");

            builder.UseSetting("ConnectionStrings:InventoryWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:InventoryRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);

            // Present so AddTeckMessaging attaches the standard (broker-backed) runtime — the
            // production dispatch path — rather than the local-only fallback.
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitConnectionString);

            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "inventory-api");

            // The inventory host resolves Finbuckle multi-tenant services per request; register the
            // infrastructure so the host boots. No strategy/store is configured (not needed here).
            builder.ConfigureTestServices(services => services.AddMultiTenant<TenantDetails>());
        }
    }
}
