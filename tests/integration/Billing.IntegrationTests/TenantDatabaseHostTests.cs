extern alias TenantDatabaseHost;
using TenantDatabaseDbContext = TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext;

using System.Diagnostics;

using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Security.Cryptography;
using JasperFx.CommandLine;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Npgsql;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Teck.TenantDatabase.Reconciler;
using Wolverine;
using Xunit;

namespace Billing.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class TenantDatabaseHostTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task WorkerMigrateCommand_AppliesSchemaWithoutHostStartupDdl()
    {
        string database = "tenant_worker_migrate_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            string connection = fixture.GetDatabaseConnectionString(database);
            var hostAssembly = typeof(TenantDatabaseHost::Program).Assembly.Location;
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
                    ["ConnectionStrings__TenantDatabaseWrite"] = connection,
                    ["ConnectionStrings__Default"] = connection,
                }
            })!;
            string stdout = await process.StandardOutput.ReadToEndAsync();
            string stderr = await process.StandardError.ReadToEndAsync();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
            await process.WaitForExitAsync(timeout.Token);
            Assert.True(process.ExitCode == 0, $"Migration command failed. stdout: {stdout} stderr: {stderr}");

            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var verify = new NpgsqlCommand("SELECT to_regclass('public.tenant_database_requests') IS NOT NULL", db);
            Assert.True((bool)(await verify.ExecuteScalarAsync())!);
            await using var history = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\"", db);
            Assert.Equal(1L, (long)(await history.ExecuteScalarAsync())!);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }
    [Fact]
    public async Task WorkerMigrateCommand_AdoptsSqlCreatedTableAndRetainsApprovalRowsOnRerun()
    {
        string database = "tenant_worker_legacy_" + Guid.NewGuid().ToString("N");
        await CreateDatabaseAsync(database);
        try
        {
            string connection = fixture.GetDatabaseConnectionString(database);
            await using (var legacy = new NpgsqlConnection(connection))
            {
                await legacy.OpenAsync();
                await using var create = new NpgsqlCommand("""
                    CREATE TABLE public.tenant_database_requests (
                        subscription_id uuid PRIMARY KEY, tenant_id uuid NOT NULL, desired_state jsonb NOT NULL,
                        phase text NOT NULL, detail text, received_at timestamptz NOT NULL, updated_at timestamptz NOT NULL,
                        lease_until timestamptz, source_order_id uuid NOT NULL, proof text NOT NULL,
                        previous_source_order_id uuid NOT NULL, evidence_id uuid, readiness_published_at timestamptz,
                        last_cluster_uid uuid, purge_approval_id uuid, purge_proof text, purge_message jsonb,
                        purge_backup_uid uuid, purge_restore_uid uuid, purge_backup_name text,
                        purge_verified_at timestamptz, purged_at timestamptz);
                    INSERT INTO public.tenant_database_requests (
                        subscription_id, tenant_id, desired_state, phase, received_at, updated_at,
                        source_order_id, proof, previous_source_order_id, purge_approval_id, purge_proof, purge_message)
                    VALUES (
                        '00000000-0000-0000-0000-000000000101', '00000000-0000-0000-0000-000000000102',
                        '{}'::jsonb, 'Retained', now(), now(), '00000000-0000-0000-0000-000000000103',
                        'paid-proof', '00000000-0000-0000-0000-000000000104',
                        '00000000-0000-0000-0000-000000000105', 'approval-proof', '{"approval":"retained"}'::jsonb);
                    """, legacy);
                await create.ExecuteNonQueryAsync();
            }

            var firstMigration = await RunMigrationCommandAsync(connection);
            Assert.True(firstMigration.ExitCode == 0, firstMigration.Output);
            var secondMigration = await RunMigrationCommandAsync(connection);
            Assert.True(secondMigration.ExitCode == 0, secondMigration.Output);

            await using var verify = new NpgsqlConnection(connection);
            await verify.OpenAsync();
            await using var query = new NpgsqlCommand("""
                SELECT phase, purge_approval_id, purge_proof, purge_message->>'approval'
                FROM public.tenant_database_requests WHERE subscription_id = '00000000-0000-0000-0000-000000000101'
                """, verify);
            await using var row = await query.ExecuteReaderAsync();
            Assert.True(await row.ReadAsync());
            Assert.Equal("Retained", row.GetString(0));
            Assert.Equal(Guid.Parse("00000000-0000-0000-0000-000000000105"), row.GetGuid(1));
            Assert.Equal("approval-proof", row.GetString(2));
            Assert.Equal("retained", row.GetString(3));
            await row.DisposeAsync();
            await verify.CloseAsync();
            await using var history = new NpgsqlConnection(connection);
            await history.OpenAsync();
            await using var count = new NpgsqlCommand("SELECT count(*) FROM \"__EFMigrationsHistory\"", history);
            Assert.Equal(1L, (long)(await count.ExecuteScalarAsync())!);
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [Fact]
    public async Task WorkerMigrateCommand_RejectsIncompatibleTableWithoutRecordingMigration()
    {
        string database = "tenant_worker_incompatible_" + Guid.NewGuid().ToString("N");
        await CreateDatabaseAsync(database);
        try
        {
            string connection = fixture.GetDatabaseConnectionString(database);
            await using (var legacy = new NpgsqlConnection(connection))
            {
                await legacy.OpenAsync();
                await using var create = new NpgsqlCommand("CREATE TABLE public.tenant_database_requests (subscription_id text PRIMARY KEY)", legacy);
                await create.ExecuteNonQueryAsync();
            }

            var result = await RunMigrationCommandAsync(connection);
            Assert.True(result.ExitCode != 0, result.Output);
            await using var verify = new NpgsqlConnection(connection);
            await verify.OpenAsync();
            await using var query = new NpgsqlCommand("SELECT to_regclass('public.__EFMigrationsHistory') IS NULL", verify);
            Assert.True((bool)(await query.ExecuteScalarAsync())!);
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

    private async Task DropDatabaseAsync(string database)
    {
        await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
        await drop.ExecuteNonQueryAsync();
    }

    private static async Task<(int ExitCode, string Output)> RunMigrationCommandAsync(string connection)
    {
        var hostAssembly = typeof(TenantDatabaseHost::Program).Assembly.Location;
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
                ["ConnectionStrings__TenantDatabaseWrite"] = connection,
                ["ConnectionStrings__Default"] = connection,
            }
        })!;
        string stdout = await process.StandardOutput.ReadToEndAsync();
        string stderr = await process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await process.WaitForExitAsync(timeout.Token);
        return (process.ExitCode, $"stdout: {stdout} stderr: {stderr}");
    }

    [Fact]
    public async Task WorkerMigration_MatchesCurrentModelAndPendingPurgeIndexFilter()
    {
        string database = "tenant_worker_model_" + Guid.NewGuid().ToString("N");
        await CreateDatabaseAsync(database);
        try
        {
            string connection = fixture.GetDatabaseConnectionString(database);
            var options = new DbContextOptionsBuilder<TenantDatabaseDbContext>().UseNpgsql(connection).Options;
            await using var context = new TenantDatabaseDbContext(options);
            var indexes = context.Model.FindEntityType(typeof(TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseRequestRow))!.GetIndexes().ToArray();
            Assert.Equal(
                new[]
                {
                    "ix_tenant_database_requests_pending",
                    "ix_tenant_database_requests_pending_purge",
                    "ix_tenant_database_requests_unpublished",
                },
                indexes.Select(index => index.GetDatabaseName()).OrderBy(name => name).ToArray());
            Assert.False(context.Database.HasPendingModelChanges());
            await context.Database.MigrateAsync();
            await using var db = new NpgsqlConnection(connection);
            await db.OpenAsync();
            await using var indexesQuery = new NpgsqlCommand("SELECT i.relname, pg_get_expr(x.indpred, x.indrelid) FROM pg_index x JOIN pg_class i ON i.oid = x.indexrelid JOIN pg_class t ON t.oid = x.indrelid WHERE t.oid = 'public.tenant_database_requests'::regclass AND i.relname LIKE 'ix_tenant_database_requests_%' ORDER BY i.relname", db);
            await using var indexRows = await indexesQuery.ExecuteReaderAsync();
            var actualIndexes = new List<(string Name, string Definition)>();
            while (await indexRows.ReadAsync())
            {
                actualIndexes.Add((indexRows.GetString(0), indexRows.IsDBNull(1) ? string.Empty : indexRows.GetString(1)));
            }
            await indexRows.DisposeAsync();
            var queueIndexes = actualIndexes.Where(index => index.Name.StartsWith("ix_tenant_database_requests_", StringComparison.Ordinal)).ToArray();
            Assert.Equal(3, queueIndexes.Length);
            Assert.Contains(queueIndexes, index => index.Name == "ix_tenant_database_requests_unpublished" && index.Definition.Contains("readiness_published_at IS NULL", StringComparison.Ordinal));
            Assert.Contains(queueIndexes, index => index.Name == "ix_tenant_database_requests_pending" && index.Definition.Contains("phase", StringComparison.Ordinal) && index.Definition.Contains("NotManaged", StringComparison.Ordinal));
            Assert.Contains(queueIndexes, index => index.Name == "ix_tenant_database_requests_pending_purge" && index.Definition.Contains("purge_message IS NOT NULL", StringComparison.Ordinal));
        }
        finally
        {
            await DropDatabaseAsync(database);
        }
    }

    [Fact]
    public async Task WorkerHost_ExposesReadyAndDurablyAdmitsOnlyBillingSignedPaidPeriod()
    {
        string database = "tenant_worker_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            string connection = fixture.GetDatabaseConnectionString(database);
            await using (var migration = new TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext(
                new DbContextOptionsBuilder<TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext>().UseNpgsql(connection).Options))
                await migration.Database.MigrateAsync();
            JasperFxEnvironment.AutoStartHost = true;
            using var signingKey = RSA.Create(2048);
            using var purgeKey = RSA.Create(2048);
            await using var host = new WebApplicationFactory<TenantDatabaseHost::Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development");
                web.UseSetting("ConnectionStrings:TenantDatabaseWrite", connection);
                web.UseSetting("ConnectionStrings:Default", connection);
                web.UseSetting("PaymentProof:TrustedPublicKeys:0", signingKey.ExportSubjectPublicKeyInfoPem());
                web.UseSetting("PurgeApproval:TrustedPublicKeys:0", purgeKey.ExportSubjectPublicKeyInfoPem());
                web.UseSetting("PurgeApproval:RecoveryDays", "30");
                web.UseSetting("BillingPurgeFenceClient:TokenEndpoint", "https://identity.test/token");
                web.UseSetting("BillingPurgeFenceClient:ClientId", "tenant-database-worker");
                web.UseSetting("BillingPurgeFenceClient:ClientSecret", "test-worker-secret");
                web.UseSetting("BillingPurgeFenceClient:Scope", "billing:purge-fence");
                web.UseSetting("BillingPurgeFenceClient:BillingBaseUrl", "http://billing-api/");
                web.UseSetting("TenantDatabase:VolumeSnapshotClass", "test-snapshot-class");
                web.UseSetting("Readiness:DatabaseKey", Convert.ToBase64String(RandomNumberGenerator.GetBytes(32)));
                web.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITenantCnpgBackend>();
                    services.AddSingleton<ITenantCnpgBackend, UnreadyBackend>();
                    services.RemoveAll<ITenantSchemaMigrator>();
                    services.AddSingleton<ITenantSchemaMigrator, NoMigrationBeforeCredentials>();
                });
            });
            using HttpClient client = host.CreateClient();
            Assert.DoesNotContain(host.Services.GetServices<Microsoft.Extensions.Hosting.IHostedService>(),
                service => service is TenantPurgePollingService);
            Assert.IsType<BillingPurgeFenceClient>(host.Services.GetRequiredService<IBillingPurgeFenceClient>());
            using var alive = await client.GetAsync("/alive");
            Assert.Equal(HttpStatusCode.OK, alive.StatusCode);
            using var health = await client.GetAsync("/health");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, health.StatusCode);
            using var unconfigured = await client.GetAsync("/ready");
            Assert.Equal(HttpStatusCode.ServiceUnavailable, unconfigured.StatusCode);
            IConfiguration config = host.Services.GetRequiredService<IConfiguration>();
            foreach (string service in new[] { "basket", "catalog", "inventory", "order", "pricing" })
                config[$"TenantDatabase:ServiceImages:{service}"] = $"registry.test/{service}:paid-period";
            config["Keycloak:realm"] = "test";
            config["Keycloak:auth-server-url"] = "https://keycloak.test";
            config["OpenBao:Url"] = "https://openbao.test";
            config["OpenBao:Token"] = "test-only-worker-token";
            using var ready = await client.GetAsync("/ready");
            var check = await host.Services.GetRequiredService<Microsoft.Extensions.Diagnostics.HealthChecks.HealthCheckService>()
                .CheckHealthAsync();
            Assert.True(ready.StatusCode == HttpStatusCode.OK,
                string.Join("; ", check.Entries.Select(entry => entry.Key + ": " + entry.Value.Description)));

            var now = DateTimeOffset.UtcNow;
            var paid = new SubscriptionPaymentConfirmedIntegrationEvent
            {
                TenantId = Guid.NewGuid(), TenantIdentifier = "host-paid-tenant", SourceOrderId = Guid.NewGuid(),
                SubscriptionId = Guid.NewGuid(), PlanId = "Business", PlanVersion = "2026-10", PaymentMethod = "captured",
                DatabaseStrategy = "dedicated", DatabaseProvider = "postgres", Currency = "USD", AmountMinor = 10000,
                ValidFromUtc = now, ValidUntilUtc = now.AddYears(1), ConfirmedAtUtc = now,
            };
            await using var scope = host.Services.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            await Assert.ThrowsAsync<InvalidOperationException>(() => bus.InvokeAsync(paid));
            paid.Proof = SubscriptionPaymentProof.Sign(paid, signingKey);
            await bus.InvokeAsync(paid);

            var store = scope.ServiceProvider.GetRequiredService<PostgresTenantDatabaseRequestStore>();
            var status = await store.GetStatusAsync(paid.SubscriptionId, CancellationToken.None);
            Assert.NotNull(status);
            Assert.Equal(paid.TenantId.ToString("D"), status.TenantId);
            Assert.NotEqual(TenantDatabasePhase.Ready, status.Phase);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
            await admin.OpenAsync();
            await using var terminate = new NpgsqlCommand("SELECT pg_terminate_backend(pid) FROM pg_stat_activity WHERE datname = @database", admin);
            terminate.Parameters.AddWithValue("database", database);
            await terminate.ExecuteNonQueryAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE \"{database}\"", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class UnreadyBackend : ITenantCnpgBackend
    {
        public Task ApplyClusterAsync(TenantClusterSpec spec, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> HasApplicationSecretAsync(string tenantId, Guid sourceOrderId, CancellationToken ct) => Task.FromResult(false);
        public Task<bool> IsClusterReadyAsync(string ns, string name, CancellationToken ct) => Task.FromResult(false);
        public Task<Guid?> GetReadyClusterUidAsync(string ns, string name, CancellationToken ct) => Task.FromResult<Guid?>(null);
        public Task ApplyDatabaseAsync(TenantDatabaseSpec spec, CancellationToken ct) => throw new InvalidOperationException("Unexpected database creation before credentials.");
        public Task<bool> IsDatabaseReadyAsync(string ns, string name, CancellationToken ct) => Task.FromResult(false);
        public Task SuspendAccessAsync(string tenantId, string identifier, Guid sourceOrderId, DateTimeOffset suspendedAt, CancellationToken ct) => Task.CompletedTask;
    }
    private sealed class NoMigrationBeforeCredentials : ITenantSchemaMigrator
    {
        public Task MigrateAsync(TenantDatabaseDesiredState state, Guid sourceOrderId, Guid previousSourceOrderId,
            string service, CancellationToken ct) => throw new InvalidOperationException("Unexpected schema migration before credentials.");
    }
}
