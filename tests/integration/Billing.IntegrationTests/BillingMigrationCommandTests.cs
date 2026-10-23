using System.Diagnostics;
using Npgsql;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Billing.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class BillingMigrationCommandTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task HostMigrateCommand_AppliesBillingSchemaAndExitsWithoutServing()
    {
        var database = $"billing_migrate_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var connection = fixture.GetDatabaseConnectionString(database);
            var hostAssembly = typeof(Program).Assembly.Location;
            using var process = Process.Start(new ProcessStartInfo("dotnet")
            {
                ArgumentList = { hostAssembly, "--migrate" },
                WorkingDirectory = Path.GetDirectoryName(hostAssembly)!,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                Environment =
                {
                    ["DOTNET_ENVIRONMENT"] = "Production",
                    ["ASPNETCORE_ENVIRONMENT"] = "Production",
                    ["ConnectionStrings__BillingWrite"] = connection,
                    ["ConnectionStrings__BillingRead"] = connection,
                    ["ConnectionStrings__Default"] = connection,
                    ["ConnectionStrings__RabbitMq"] = fixture.RabbitMqConnectionString,
                    ["Keycloak__realm"] = "test",
                    ["Keycloak__auth-server-url"] = "http://localhost:8080",
                    ["Keycloak__resource"] = "billing-api",
                },
            });
            Assert.NotNull(process);
            var stdoutTask = process.StandardOutput.ReadToEndAsync();
            var stderrTask = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            var stdout = await stdoutTask;
            var stderr = await stderrTask;
            Assert.True(process.ExitCode == 0, $"--migrate exited {process.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");

            await using var verify = new NpgsqlConnection(connection);
            await verify.OpenAsync();
            await using var history = new NpgsqlCommand("SELECT COUNT(*) FROM \"__EFMigrationsHistory\"", verify);
            Assert.True(Convert.ToInt64(await history.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0);
            await using var payments = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'payments')", verify);
            Assert.True((bool)(await payments.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
            await admin.OpenAsync();
            await using (var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @name AND pid <> pg_backend_pid()", admin))
            {
                terminate.Parameters.AddWithValue("name", database);
                await terminate.ExecuteNonQueryAsync();
            }
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
