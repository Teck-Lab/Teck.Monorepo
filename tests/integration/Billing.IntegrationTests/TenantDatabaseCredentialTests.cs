using System.Net;
using System.Text;
using System.Text.Json;
using Npgsql;
using Teck.Platform.IntegrationTests.Shared;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Billing.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class TenantDatabaseCredentialTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task MigratedDatabase_DoesNotPublishUntilApplicationRoleAuthenticatesAndCanReadOwnedTables()
    {
        var tenant = Guid.NewGuid();
        string suffix = tenant.ToString("N");
        string database = "tenant_" + suffix + "_basket";
        string migrationRole = "tenant_" + suffix + "_basket_migration";
        string applicationRole = "tenant_" + suffix + "_basket_app";
        const string migrationPassword = "isolated-local-migration-password-77";
        const string applicationPassword = "isolated-local-application-password-88";
        await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = $"CREATE ROLE \"{migrationRole}\" LOGIN PASSWORD '{migrationPassword}';" +
                $"CREATE ROLE \"{applicationRole}\" LOGIN PASSWORD '{applicationPassword}';";
            await create.ExecuteNonQueryAsync();
            create.CommandText = $"CREATE DATABASE \"{database}\" OWNER \"{migrationRole}\"";
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            var adminConnection = fixture.GetDatabaseConnectionString(database);
            await using (var migration = new NpgsqlConnection(adminConnection))
            {
                await migration.OpenAsync();
                await using var createTable = new NpgsqlCommand("CREATE TABLE app_data (id int PRIMARY KEY); INSERT INTO app_data VALUES (42)", migration);
                await createTable.ExecuteNonQueryAsync();
                await using var transfer = new NpgsqlCommand($"ALTER TABLE app_data OWNER TO \"{migrationRole}\"", migration);
                await transfer.ExecuteNonQueryAsync();
            }

            var order = Guid.NewGuid();
            var transport = new RoleSecrets(suffix, order, migrationRole, applicationRole, migrationPassword, "wrong-local-application-password-99");
            using var kube = new HttpClient(transport) { BaseAddress = new Uri("https://kubernetes.test/") };
            var published = new RecordingOpenBao();
            var server = new NpgsqlConnectionStringBuilder(fixture.AdminConnectionString);
            var sut = new KubernetesTenantMigrationConnections(kube, published, _ => server.Host!, server.Port,
                SslMode.Disable, null);
            var desired = new TenantDatabaseDesiredState(tenant.ToString("D"), "paid-business", "dedicated", "postgres", 0,
                TenantSubscriptionStatus.Active, null);

            string secretName = await sut.EnsureMigrationConnectionAsync(desired, order, "basket", CancellationToken.None);
            Assert.NotEmpty(secretName);
            using var created = JsonDocument.Parse(Assert.Single(transport.Created));
            string encoded = created.RootElement.GetProperty("data").GetProperty("connection").GetString()!;
            var migrationString = new NpgsqlConnectionStringBuilder(Encoding.UTF8.GetString(Convert.FromBase64String(encoded)));
            Assert.Equal(database, migrationString.Database);
            Assert.Equal(migrationRole, migrationString.Username);

            await Assert.ThrowsAnyAsync<NpgsqlException>(() => sut.VerifyAndPublishApplicationConnectionAsync(
                desired, order, Guid.Empty, "basket", CancellationToken.None));
            Assert.Empty(published.Records);
            transport.AppPassword = applicationPassword;
            await sut.VerifyAndPublishApplicationConnectionAsync(desired, order, Guid.Empty, "basket", CancellationToken.None);

            var saved = Assert.Single(published.Records);
            Assert.Equal("tenants/paid-business/basket", saved.Path);
            Assert.Equal(order, saved.SourceOrderId);
            var appString = new NpgsqlConnectionStringBuilder(saved.Write);
            Assert.Equal(applicationRole, appString.Username);
            await using var appConnection = new NpgsqlConnection(saved.Write);
            await appConnection.OpenAsync();
            await using var query = new NpgsqlCommand("SELECT id FROM app_data", appConnection);
            Assert.Equal(42, await query.ExecuteScalarAsync());
        }
        finally
        {
            await using var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database", admin);
            terminate.Parameters.AddWithValue("database", database);
            await terminate.ExecuteNonQueryAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\"; DROP ROLE \"{applicationRole}\"; DROP ROLE \"{migrationRole}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class RecordingOpenBao : ITenantOpenBaoConnectionStore
    {
        public List<(string Path, Guid SourceOrderId, string Write)> Records { get; } = [];
        public Task WriteAsync(string path, Guid sourceOrderId, Guid previousSourceOrderId, string write, CancellationToken ct)
        {
            Records.Add((path, sourceOrderId, write));
            return Task.CompletedTask;
        }
        public Task RevokeAsync(string path, Guid sourceOrderId, CancellationToken ct) =>
            throw new InvalidOperationException("Unexpected OpenBao revocation during active migration.");
    }

    private sealed class RoleSecrets(string suffix, Guid sourceOrderId, string migrationRole, string applicationRole,
        string migrationPassword, string initialAppPassword) : HttpMessageHandler
    {
        public string AppPassword { get; set; } = initialAppPassword;
        public List<string> Created { get; } = [];
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            if (request.Method == HttpMethod.Post)
            {
                Created.Add(await request.Content!.ReadAsStringAsync(ct));
                return new HttpResponseMessage(HttpStatusCode.Created);
            }
            string path = request.RequestUri!.AbsolutePath;
            string migration = $"tenant-{suffix}-basket-migration-{sourceOrderId:N}";
            string app = $"tenant-{suffix}-basket-app-{sourceOrderId:N}";
            string? role = path.EndsWith(migration, StringComparison.Ordinal) ? migrationRole :
                path.EndsWith(app, StringComparison.Ordinal) ? applicationRole : null;
            if (role is null) return new HttpResponseMessage(HttpStatusCode.NotFound);
            string password = role == migrationRole ? migrationPassword : AppPassword;
            var json = JsonSerializer.Serialize(new { type = "kubernetes.io/basic-auth", data = new
            {
                username = Convert.ToBase64String(Encoding.UTF8.GetBytes(role)),
                password = Convert.ToBase64String(Encoding.UTF8.GetBytes(password)),
            } });
            return new HttpResponseMessage(HttpStatusCode.OK) { Content = new StringContent(json, Encoding.UTF8, "application/json") };
        }
    }
}
