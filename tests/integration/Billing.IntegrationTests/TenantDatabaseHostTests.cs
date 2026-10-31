extern alias TenantDatabaseHost;

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
            JasperFxEnvironment.AutoStartHost = true;
            string connection = fixture.GetDatabaseConnectionString(database);
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
