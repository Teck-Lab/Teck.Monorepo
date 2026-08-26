using System.Diagnostics;
using Npgsql;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Notifications.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class MigrationCommandTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task HostMigrateCommand_ExitsAndCreatesNotificationSchema()
    {
        var database = $"notification_migrate_{Guid.NewGuid():N}";
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
                    ["ASPNETCORE_ENVIRONMENT"] = "Production",
                    ["ConnectionStrings__NotificationWrite"] = connection,
                    ["ConnectionStrings__NotificationRead"] = connection,
                    ["ConnectionStrings__Default"] = connection,
                    ["Keycloak__realm"] = "test",
                    ["Keycloak__auth-server-url"] = "http://localhost:8080",
                    ["Keycloak__resource"] = "notification-api",
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

            Assert.Contains("customer_contacts", tables);
            Assert.Contains("notification_deliveries", tables);
            Assert.Contains("__EFMigrationsHistory", tables);
        }
        finally
        {
            await fixture.DropTestDatabaseAsync(database);
        }
    }
}
