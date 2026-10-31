extern alias TenantDatabaseHost;

using System.Net;
using System.Security.Cryptography;
using JasperFx.CommandLine;
using MemoryPack;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;
using Npgsql;
using RabbitMQ.Client;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Teck.TenantDatabase.Reconciler;
using Wolverine.RabbitMQ;
using Wolverine;
using Xunit;

namespace Billing.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class TenantDatabaseReadinessDeliveryTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task ReadyWorker_PublishesSignedBillingCorrelatedEventToRabbitMqAndAcknowledgesPostgresRequest()
    {
        string database = "tenant_delivery_" + Guid.NewGuid().ToString("N");
        await using (var admin = new NpgsqlConnection(fixture.AdminConnectionString))
        {
            await admin.OpenAsync();
            await using var create = new NpgsqlCommand($"CREATE DATABASE \"{database}\"", admin);
            await create.ExecuteNonQueryAsync();
        }

        try
        {
            string connection = fixture.GetDatabaseConnectionString(database);
            var options = new Microsoft.EntityFrameworkCore.DbContextOptionsBuilder<TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext>()
                .UseNpgsql(connection).Options;
            await using (var migrationContext = new TenantDatabaseHost::Teck.TenantDatabase.Host.Database.TenantDatabaseDbContext(options))
            {
                await migrationContext.Database.MigrateAsync();
            }

            JasperFxEnvironment.AutoStartHost = true;
            byte[] readinessKey = RandomNumberGenerator.GetBytes(32);
            using var paidKey = RSA.Create(2048);
            using var purgeKey = RSA.Create(2048);
            var clusterUid = Guid.NewGuid();
            await using var host = new WebApplicationFactory<TenantDatabaseHost::Program>().WithWebHostBuilder(web =>
            {
                web.UseEnvironment("Development");
                web.UseSetting("ConnectionStrings:TenantDatabaseWrite", fixture.GetDatabaseConnectionString(database));
                web.UseSetting("ConnectionStrings:Default", fixture.GetDatabaseConnectionString(database));
                web.UseSetting("ConnectionStrings:rabbitmq", fixture.RabbitMqConnectionString);
                web.UseSetting("PaymentProof:TrustedPublicKeys:0", paidKey.ExportSubjectPublicKeyInfoPem());
                web.UseSetting("PurgeApproval:TrustedPublicKeys:0", purgeKey.ExportSubjectPublicKeyInfoPem());
                web.UseSetting("PurgeApproval:RecoveryDays", "30");
                web.UseSetting("BillingPurgeFenceClient:TokenEndpoint", "https://identity.test/token");
                web.UseSetting("BillingPurgeFenceClient:ClientId", "tenant-database-worker");
                web.UseSetting("BillingPurgeFenceClient:ClientSecret", "test-only-worker-secret");
                web.UseSetting("BillingPurgeFenceClient:Scope", "billing:purge-fence");
                web.UseSetting("BillingPurgeFenceClient:BillingBaseUrl", "http://billing-api/");
                web.UseSetting("TenantDatabase:VolumeSnapshotClass", "test-snapshot-class");
                web.UseSetting("Readiness:DatabaseKey", Convert.ToBase64String(readinessKey));
                web.UseSetting("TenantDatabase:Polling:PollInterval", "00:00:00.1");
                foreach (string service in new[] { "basket", "catalog", "inventory", "order", "pricing" })
                    web.UseSetting($"TenantDatabase:ServiceImages:{service}", $"registry.test/{service}:paid-period");
                web.UseSetting("Keycloak:realm", "test");
                web.UseSetting("Keycloak:auth-server-url", "https://keycloak.test");
                web.UseSetting("OpenBao:Url", "https://openbao.test");
                web.UseSetting("OpenBao:Token", "test-only-worker-token");
                web.ConfigureTestServices(services =>
                {
                    services.RemoveAll<ITenantCnpgBackend>();
                    services.AddSingleton<ITenantCnpgBackend>(new ReadyBackend(clusterUid));
                    services.RemoveAll<ITenantSchemaMigrator>();
                    services.AddSingleton<ITenantSchemaMigrator, CompletedMigrations>();
                });
            });
            using var client = host.CreateClient();
            using var health = await client.GetAsync("/ready");
            Assert.Equal(HttpStatusCode.OK, health.StatusCode);

            var rabbitFactory = new ConnectionFactory { Uri = new Uri(fixture.RabbitMqConnectionString, UriKind.Absolute) };
            await using var rabbit = await rabbitFactory.CreateConnectionAsync();
            await using var channel = await rabbit.CreateChannelAsync();
            const string exchange = "SharedKernel.Events.TenantProvisioningReadyIntegrationEvent";
            await channel.ExchangeDeclareAsync(exchange, RabbitMQ.Client.ExchangeType.Fanout, durable: true);
            var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true);
            await channel.QueueBindAsync(queue.QueueName, exchange, string.Empty);

            var now = DateTimeOffset.UtcNow;
            var paid = new SubscriptionPaymentConfirmedIntegrationEvent
            {
                TenantId = Guid.NewGuid(), TenantIdentifier = "readiness-delivery-tenant",
                SourceOrderId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), PlanId = "Business",
                PlanVersion = "2026-10", PaymentMethod = "captured", DatabaseStrategy = "dedicated",
                DatabaseProvider = "postgres", Currency = "USD", AmountMinor = 10000,
                ValidFromUtc = now, ValidUntilUtc = now.AddYears(1), ConfirmedAtUtc = now,
            };
            paid.Proof = SubscriptionPaymentProof.Sign(paid, paidKey);
            await using var scope = host.Services.CreateAsyncScope();
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(paid);

            using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(20));
            BasicGetResult? delivery = null;
            while (!deadline.IsCancellationRequested && delivery is null)
            {
                delivery = await channel.BasicGetAsync(queue.QueueName, autoAck: true);
                if (delivery is null) await Task.Delay(100, deadline.Token);
            }
            Assert.NotNull(delivery);
            var readiness = MemoryPackSerializer.Deserialize<TenantProvisioningReadyIntegrationEvent>(delivery.Body.Span);
            Assert.NotNull(readiness);
            Assert.Equal(paid.TenantId, readiness.TenantId);
            Assert.Equal(paid.SourceOrderId, readiness.SourceOrderId);
            Assert.Equal(paid.SubscriptionId, readiness.SubscriptionId);
            Assert.Equal("database", readiness.Provisioner);
            Assert.Equal(clusterUid, readiness.EvidenceId);
            Assert.True(TenantReadinessProof.Verify(readiness, readinessKey));

            await using var db = new NpgsqlConnection(fixture.GetDatabaseConnectionString(database));
            await db.OpenAsync();
            using var acknowledged = new CancellationTokenSource(TimeSpan.FromSeconds(10));
            bool published = false;
            while (!acknowledged.IsCancellationRequested && !published)
            {
                await using var check = new NpgsqlCommand("SELECT readiness_published_at IS NOT NULL FROM tenant_database_requests WHERE subscription_id = @id", db);
                check.Parameters.AddWithValue("id", paid.SubscriptionId);
                published = (bool)(await check.ExecuteScalarAsync())!;
                if (!published) await Task.Delay(100, acknowledged.Token);
            }
            Assert.True(published, "The signed event must be acknowledged in PostgreSQL after RabbitMQ publication.");
            Assert.DoesNotContain(await scope.ServiceProvider.GetRequiredService<ITenantDatabaseRequestStore>()
                .GetPendingAsync(32, CancellationToken.None), request => request.SubscriptionId == paid.SubscriptionId);
            Assert.Null(await channel.BasicGetAsync(queue.QueueName, autoAck: true));

            var suspendedAt = DateTimeOffset.UtcNow.AddDays(-31);
            var suspension = new SubscriptionAccessSuspendedIntegrationEvent
            {
                TenantId = paid.TenantId, TenantIdentifier = paid.TenantIdentifier,
                SubscriptionId = paid.SubscriptionId, SourceOrderId = paid.SourceOrderId,
                Reason = "cancelled", SuspendedAtUtc = suspendedAt,
            };
            suspension.Proof = SubscriptionSuspensionProof.Sign(suspension, paidKey);
            await scope.ServiceProvider.GetRequiredService<IMessageBus>().InvokeAsync(suspension);
            using var retainedDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            while (!retainedDeadline.IsCancellationRequested &&
                (await scope.ServiceProvider.GetRequiredService<PostgresTenantDatabaseRequestStore>()
                    .GetStatusAsync(paid.SubscriptionId, CancellationToken.None))?.Phase != TenantDatabasePhase.Retained)
                await Task.Delay(100);
            Assert.Equal(TenantDatabasePhase.Retained,
                (await scope.ServiceProvider.GetRequiredService<PostgresTenantDatabaseRequestStore>()
                    .GetStatusAsync(paid.SubscriptionId, CancellationToken.None))!.Phase);

            var approval = new TenantPurgeApprovedIntegrationEvent
            {
                ApprovalId = Guid.NewGuid(), TenantId = paid.TenantId, TenantIdentifier = paid.TenantIdentifier,
                SubscriptionId = paid.SubscriptionId, SourceOrderId = paid.SourceOrderId,
                SuspendedAtUtc = suspendedAt, EligibleAtUtc = suspendedAt.AddDays(30), ApprovedAtUtc = DateTimeOffset.UtcNow,
                FirstApproverSubjectId = "manager-one", SecondApproverSubjectId = "manager-two",
            };
            approval.Proof = TenantPurgeApprovalProof.Sign(approval, purgeKey);
            using var publisher = await Host.CreateDefaultBuilder()
                .UseWolverine(options => options.UseRabbitMq(new Uri(fixture.RabbitMqConnectionString, UriKind.Absolute))
                    .AutoProvision().UseConventionalRouting())
                .StartAsync();
            await publisher.Services.GetRequiredService<IMessageBus>().PublishAsync(approval);
            using var acceptedDeadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
            IReadOnlyList<TenantPurgeApprovedIntegrationEvent> queued = [];
            while (!acceptedDeadline.IsCancellationRequested && queued.Count == 0)
            {
                queued = await scope.ServiceProvider.GetRequiredService<ITenantPurgeStore>()
                    .GetPendingPurgeApprovalsAsync(10, CancellationToken.None);
                if (queued.Count == 0) await Task.Delay(100);
            }
            Assert.Equal(approval.ApprovalId, Assert.Single(queued).ApprovalId);
            Assert.Equal(TenantDatabasePhase.Retained,
                (await scope.ServiceProvider.GetRequiredService<PostgresTenantDatabaseRequestStore>()
                    .GetStatusAsync(paid.SubscriptionId, CancellationToken.None))!.Phase);
        }
        finally
        {
            await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
            await admin.OpenAsync();
            await using var drop = new NpgsqlCommand($"DROP DATABASE IF EXISTS \"{database}\" WITH (FORCE)", admin);
            await drop.ExecuteNonQueryAsync();
        }
    }

    private sealed class ReadyBackend(Guid uid) : ITenantCnpgBackend
    {
        public Task ApplyClusterAsync(TenantClusterSpec spec, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> HasApplicationSecretAsync(string tenantId, Guid sourceOrderId, CancellationToken ct) => Task.FromResult(true);
        public Task<bool> IsClusterReadyAsync(string ns, string name, CancellationToken ct) => Task.FromResult(true);
        public Task<Guid?> GetReadyClusterUidAsync(string ns, string name, CancellationToken ct) => Task.FromResult<Guid?>(uid);
        public Task ApplyDatabaseAsync(TenantDatabaseSpec spec, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> IsDatabaseReadyAsync(string ns, string name, CancellationToken ct) => Task.FromResult(true);
        public Task SuspendAccessAsync(string tenantId, string identifier, Guid sourceOrderId, DateTimeOffset suspendedAt, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CompletedMigrations : ITenantSchemaMigrator
    {
        public Task MigrateAsync(TenantDatabaseDesiredState state, Guid sourceOrderId, Guid previousSourceOrderId,
            string service, CancellationToken ct) => Task.CompletedTask;
    }
}
