using System.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using JasperFx.CommandLine;
using Npgsql;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Inventories.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class InventoryMigrationCommandTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task HostMigrateCommand_CreatesReservationSchemaWithoutStartingSweep()
    {
        var database = $"inventory_migrate_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        var connection = fixture.GetDatabaseConnectionString(database);
        try
        {
            var hostAssembly = typeof(Program).Assembly.Location;
            using var process = Process.Start(new ProcessStartInfo("dotnet", $"\"{hostAssembly}\" --migrate")
            {
                WorkingDirectory = Path.GetDirectoryName(hostAssembly)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                Environment =
                {
                    ["DOTNET_ENVIRONMENT"] = "Production",
                    ["ASPNETCORE_ENVIRONMENT"] = "Production",
                    ["ConnectionStrings__InventoryWrite"] = connection,
                    ["ConnectionStrings__InventoryRead"] = connection,
                    ["ConnectionStrings__Default"] = connection,
                    ["Keycloak__realm"] = "test",
                    ["Keycloak__auth-server-url"] = "http://localhost:8080",
                    ["Keycloak__resource"] = "inventory-api",
                    ["ConnectionStrings__RabbitMq"] = fixture.RabbitMqConnectionString,
                }
            });
            Assert.NotNull(process);

            var stdoutTask = process!.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));

            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            Assert.True(
                process.ExitCode == 0,
                $"--migrate exited with code {process.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");

            await using var verify = new NpgsqlConnection(connection);
            await verify.OpenAsync();
            await AssertTableExistsAsync(verify, "Reservations");
            await AssertTableExistsAsync(verify, "ReservationLines");
            await AssertTableExistsAsync(verify, "__EFMigrationsHistory");
            await using var migrations = new NpgsqlCommand("SELECT COUNT(*) FROM \"__EFMigrationsHistory\"", verify);
            Assert.True(Convert.ToInt32(await migrations.ExecuteScalarAsync()) > 0, "--migrate must apply Inventory migrations.");
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    private async Task DropDatabaseAsync(string database)
    {
        await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using var terminate = new NpgsqlCommand(
            "SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database AND pid <> pg_backend_pid()",
            admin);
        terminate.Parameters.AddWithValue("database", database);
        await terminate.ExecuteNonQueryAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\"", admin);
        await drop.ExecuteNonQueryAsync();
    }

    [Fact]
    public async Task DevelopmentHost_OnStartup_MigratesFreshDatabaseBeforeReservationSweep()
    {
        var database = $"inventory_startup_{Guid.NewGuid():N}";
        await CreateDatabaseAsync(database);
        var connection = fixture.GetDatabaseConnectionString(database);
        try
        {
            using var factory = new InventoryStartupWebApplicationFactory(connection, fixture.RabbitMqConnectionString);
            using var client = factory.CreateClient();

            await using var admin = new NpgsqlConnection(connection);
            await admin.OpenAsync();
            await AssertTableExistsAsync(admin, "Reservations");
            await AssertTableExistsAsync(admin, "ReservationLines");
            await AssertTableExistsAsync(admin, "__EFMigrationsHistory");
            await using var migrations = new NpgsqlCommand("SELECT COUNT(*) FROM \"__EFMigrationsHistory\"", admin);
            Assert.True(Convert.ToInt32(await migrations.ExecuteScalarAsync()) > 0, "Host startup must apply EF migrations.");
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    private async Task CreateDatabaseAsync(string database)
    {
        await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
        await create.ExecuteNonQueryAsync();
    }

    private static async Task AssertTableExistsAsync(NpgsqlConnection connection, string table)
    {
        await using var command = new NpgsqlCommand(
            "SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_schema = 'public' AND table_name = @table)",
            connection);
        command.Parameters.AddWithValue("table", table);
        Assert.True((bool)(await command.ExecuteScalarAsync())!, $"Expected migrated table {table}.");
    }

    private sealed class InventoryStartupWebApplicationFactory(string databaseConnectionString, string rabbitConnectionString)
        : WebApplicationFactory<Program>
    {
        static InventoryStartupWebApplicationFactory()
        {
            JasperFxEnvironment.AutoStartHost = true;
        }

        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.UseEnvironment("Development");
            builder.UseSetting("ConnectionStrings:InventoryWrite", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:InventoryRead", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:Default", databaseConnectionString);
            builder.UseSetting("ConnectionStrings:rabbitmq", rabbitConnectionString);
            builder.UseSetting("Keycloak:realm", "test");
            builder.UseSetting("Keycloak:auth-server-url", "http://localhost:8080");
            builder.UseSetting("Keycloak:resource", "inventory-api");
        }
    }
}
