using System.Diagnostics;
using Npgsql;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Catalog.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class CatalogMigrationCommandTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task HostMigrateCommand_AppliesCatalogSchemaAndExitsWithoutServing()
    {
        var database = $"catalog_migrate_{Guid.NewGuid():N}";
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
            for (var invocation = 0; invocation < 2; invocation++)
            {
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
                        ["ConnectionStrings__CatalogWrite"] = connection,
                        ["ConnectionStrings__CatalogRead"] = connection,
                        ["ConnectionStrings__Default"] = connection,
                        ["ConnectionStrings__RabbitMq"] = fixture.RabbitMqConnectionString,
                        ["Keycloak__realm"] = "test",
                        ["Keycloak__auth-server-url"] = "http://localhost:8080",
                        ["Keycloak__resource"] = "catalog-api",
                    },
                });
                Assert.NotNull(process);
                var stdoutTask = process.StandardOutput.ReadToEndAsync();
                var stderrTask = process.StandardError.ReadToEndAsync();
                await process.WaitForExitAsync().WaitAsync(TimeSpan.FromSeconds(60));
                var stdout = await stdoutTask;
                var stderr = await stderrTask;
                Assert.True(process.ExitCode == 0,
                    $"--migrate invocation {invocation + 1} exited {process.ExitCode}.\nstdout:\n{stdout}\nstderr:\n{stderr}");
            }

            await using var verify = new NpgsqlConnection(connection);
            await verify.OpenAsync();
            await using var history = new NpgsqlCommand("SELECT COUNT(*) FROM \"__EFMigrationsHistory\"", verify);
            Assert.True(Convert.ToInt64(await history.ExecuteScalarAsync(), System.Globalization.CultureInfo.InvariantCulture) > 0);
            await using var products = new NpgsqlCommand("SELECT EXISTS (SELECT 1 FROM information_schema.tables WHERE table_name = 'Products')", verify);
            Assert.True((bool)(await products.ExecuteScalarAsync())!);
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
