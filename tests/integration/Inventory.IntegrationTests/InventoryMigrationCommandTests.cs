using System.Diagnostics;
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
                },
            });
            Assert.NotNull(process);

            await process!.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            var output = await process.StandardOutput.ReadToEndAsync();
            var error = await process.StandardError.ReadToEndAsync();
            Assert.True(process.ExitCode == 0, $"--migrate exited {process.ExitCode}.{Environment.NewLine}stdout:{Environment.NewLine}{output}{Environment.NewLine}stderr:{Environment.NewLine}{error}");

            await using var verify = new NpgsqlConnection(connection);
            await verify.OpenAsync();
            await using var command = new NpgsqlCommand("SELECT tablename FROM pg_tables WHERE schemaname = 'public'", verify);
            await using var reader = await command.ExecuteReaderAsync();
            var tables = new List<string>();
            while (await reader.ReadAsync())
            {
                tables.Add(reader.GetString(0));
            }

            Assert.Contains("Reservations", tables);
            Assert.Contains("ReservationLines", tables);
            Assert.Contains("__EFMigrationsHistory", tables);
        }
        finally
        {
            await fixture.DropTestDatabaseAsync(database);
        }
    }
}
