using System.Diagnostics;
using Npgsql;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Baskets.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class BasketMigrationCommandTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task HostMigrateCommand_AppliesBasketSchemaAndExitsWithoutServing()
    {
        string database = $"basket_migrate_{Guid.NewGuid():N}";
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            string connection = fixture.GetDatabaseConnectionString(database);
            string hostAssembly = typeof(Program).Assembly.Location;
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
                    ["ConnectionStrings__BasketWrite"] = connection,
                    ["ConnectionStrings__BasketRead"] = connection,
                    ["ConnectionStrings__Default"] = connection,
                    ["ConnectionStrings__RabbitMq"] = fixture.RabbitMqConnectionString,
                    ["Keycloak__realm"] = "test",
                    ["Keycloak__auth-server-url"] = "http://localhost:8080",
                    ["Keycloak__resource"] = "basket-api",
                },
            });
            Assert.NotNull(process);
            Task<string> stdout = process.StandardOutput.ReadToEndAsync();
            Task<string> stderr = process.StandardError.ReadToEndAsync();
            await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
            Assert.True(process.ExitCode == 0,
                $"Basket --migrate exited {process.ExitCode}.\nstdout:\n{await stdout}\nstderr:\n{await stderr}");

            await using var verify = new NpgsqlConnection(connection);
            await verify.OpenAsync();
            await using var history = new NpgsqlCommand("SELECT COUNT(*) FROM \"__EFMigrationsHistory\"", verify);
            Assert.True(Convert.ToInt64(await history.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0);
            await using var baskets = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'Baskets')", verify);
            Assert.True((bool)(await baskets.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
            await admin.OpenAsync();
            await using (var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database", admin))
            {
                terminate.Parameters.AddWithValue("database", database);
                await terminate.ExecuteNonQueryAsync();
            }
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
}
