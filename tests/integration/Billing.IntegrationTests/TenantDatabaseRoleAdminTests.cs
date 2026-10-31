using System.Net;
using System.Text.Json;
using Npgsql;
using Teck.Platform.IntegrationTests.Shared;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Billing.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class TenantDatabaseRoleAdminTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task BootstrapGrants_AllowTenantManagerToDisableLoginsButNotReadOrAssumeServiceRoles()
    {
        var tenant = Guid.NewGuid();
        string suffix = tenant.ToString("N");
        string bootstrap = $"tenant_{suffix}_migration";
        string manager = $"tenant_{suffix}_manager";
        string app = $"tenant_{suffix}_basket_app";
        string database = $"tenant_{suffix}_basket";
        const string managerPassword = "test-only-manager-password-1234567890";
        using var handler = new CaptureClusterHandler(manager, managerPassword);
        using var http = new HttpClient(handler) { BaseAddress = new Uri("https://kubernetes.test/") };
        var server = new NpgsqlConnectionStringBuilder(fixture.AdminConnectionString);
        var revoker = new PostgresTenantRoleRevoker(http, _ => server.Host!, server.Port, SslMode.Disable, null);
        await new KubernetesCnpgBackend(http, revoker, new UnexpectedOpenBao()).ApplyClusterAsync(new("tenant-databases", $"tenant-{suffix}", 2,
            $"tenant-{suffix}-credentials", Guid.NewGuid()), CancellationToken.None);
        using var manifest = JsonDocument.Parse(handler.Cluster!);
        string[] commands = manifest.RootElement.GetProperty("spec").GetProperty("bootstrap").GetProperty("initdb")
            .GetProperty("postInitSQL").EnumerateArray().Select(sql => sql.GetString()!).ToArray();
        await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using (var create = new NpgsqlCommand($"CREATE ROLE \"{bootstrap}\" LOGIN;", admin))
            await create.ExecuteNonQueryAsync();
        try
        {
            foreach (string sql in commands)
            {
                await using var command = new NpgsqlCommand(sql, admin);
                await command.ExecuteNonQueryAsync();
            }
            await using (var enable = new NpgsqlCommand($"ALTER ROLE \"{manager}\" LOGIN PASSWORD '{managerPassword}'; " +
                $"ALTER ROLE \"{app}\" LOGIN PASSWORD 'test-only-app-password'", admin))
                await enable.ExecuteNonQueryAsync();
            await using (var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\" OWNER \"{bootstrap}\"", admin))
                await create.ExecuteNonQueryAsync();
            var baseConnection = new NpgsqlConnectionStringBuilder(fixture.GetDatabaseConnectionString(database));
            var appConnection = new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString)
            {
                Username = app, Password = "test-only-app-password", Pooling = false,
            };
            var managerConnection = new NpgsqlConnectionStringBuilder(baseConnection.ConnectionString)
            {
                Username = manager, Password = managerPassword, Pooling = false,
            };
            await using (var grantSchema = new NpgsqlConnection(baseConnection.ConnectionString))
            {
                await grantSchema.OpenAsync();
                await using var grant = new NpgsqlCommand($"GRANT CREATE ON SCHEMA public TO \"{app}\"", grantSchema);
                await grant.ExecuteNonQueryAsync();
            }
            await using var appSession = new NpgsqlConnection(appConnection.ConnectionString);
            await appSession.OpenAsync();
            await using var managerSession = new NpgsqlConnection(managerConnection.ConnectionString);
            await managerSession.OpenAsync();
            await using (var appTable = new NpgsqlCommand("CREATE TABLE app_data (id integer); INSERT INTO app_data VALUES (42)", appSession))
                await appTable.ExecuteNonQueryAsync();
            await using (var leaked = new NpgsqlCommand("SELECT id FROM app_data", managerSession))
                await Assert.ThrowsAsync<PostgresException>(async () => await leaked.ExecuteScalarAsync());
            await using (var setRole = new NpgsqlCommand($"SET ROLE \"{app}\"", managerSession))
                await Assert.ThrowsAsync<PostgresException>(async () => await setRole.ExecuteNonQueryAsync());
            // The same worker-scoped revoker that the backend uses is exercised against real PostgreSQL.
            await revoker.RevokeAsync(tenant, CancellationToken.None);
            await revoker.RevokeAsync(tenant, CancellationToken.None);
            await using (var status = new NpgsqlCommand("SELECT count(*) FROM pg_roles WHERE rolname LIKE @prefix AND rolname <> @manager AND rolcanlogin", admin))
            {
                status.Parameters.AddWithValue("prefix", $"tenant_{suffix}_%");
                status.Parameters.AddWithValue("manager", manager);
                Assert.Equal(0L, await status.ExecuteScalarAsync());
            }
            await using (var passwords = new NpgsqlCommand("SELECT count(*) FROM pg_authid WHERE rolname LIKE @prefix AND rolname <> @manager AND rolpassword IS NOT NULL", admin))
            {
                passwords.Parameters.AddWithValue("prefix", $"tenant_{suffix}_%");
                passwords.Parameters.AddWithValue("manager", manager);
                Assert.Equal(0L, await passwords.ExecuteScalarAsync());
            }
            await using (var sessions = new NpgsqlCommand("SELECT count(*) FROM pg_stat_activity WHERE usename = @app", admin))
            {
                sessions.Parameters.AddWithValue("app", app);
                Assert.Equal(0L, await sessions.ExecuteScalarAsync());
            }
            await using var rejected = new NpgsqlConnection(appConnection.ConnectionString);
            await Assert.ThrowsAsync<PostgresException>(async () => await rejected.OpenAsync());
        }
        finally
        {
            await using var clean = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await clean.ExecuteNonQueryAsync();
            foreach (string role in new[] { manager, bootstrap }.Concat(new[] { "basket", "catalog", "inventory", "order", "pricing" }
                .SelectMany(service => new[] { $"tenant_{suffix}_{service}_app", $"tenant_{suffix}_{service}_migration" })))
            {
                await using var drop = new NpgsqlCommand($"DROP ROLE IF EXISTS \"{role}\"", admin);
                await drop.ExecuteNonQueryAsync();
            }
        }
    }
    private sealed class UnexpectedOpenBao : ITenantOpenBaoConnectionStore
    {
        public Task WriteAsync(string path, Guid sourceOrderId, Guid previousSourceOrderId, string write, CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected OpenBao credential publication.");
        public Task RevokeAsync(string path, Guid sourceOrderId, CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected OpenBao revocation during manifest test.");
    }


    private sealed class CaptureClusterHandler(string manager, string password) : HttpMessageHandler
    {
        public string? Cluster { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Get)
            {
                if (request.RequestUri!.AbsolutePath.EndsWith("-manager", StringComparison.Ordinal))
                {
                    var body = JsonSerializer.Serialize(new
                    {
                        type = "kubernetes.io/basic-auth",
                        data = new { username = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(manager)),
                            password = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(password)) },
                    });
                    return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(body) };
                }
                return new HttpResponseMessage(HttpStatusCode.NotFound);
            }
            if (request.RequestUri!.AbsolutePath.EndsWith("/clusters", StringComparison.Ordinal))
                Cluster = await request.Content!.ReadAsStringAsync(ct);
            return new HttpResponseMessage(HttpStatusCode.Created);
        }
    }
}
