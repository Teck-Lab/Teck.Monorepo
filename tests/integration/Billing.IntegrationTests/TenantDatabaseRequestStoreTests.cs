using System.Security.Cryptography;
using Npgsql;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Exercises signed tenant database intake and durable SQL replay protection.</summary>
[Collection("SharedTestcontainers")]
public sealed class TenantDatabaseRequestStoreTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task Signed_paid_request_survives_polling_and_rejects_changed_order_or_tenant()
    {
        var connectionString = fixture.GetDatabaseConnectionString("testdb_tenant_database_requests");
        await using var admin = new NpgsqlConnection(fixture.AdminConnectionString);
        await admin.OpenAsync();
        await using (var create = admin.CreateCommand())
        {
            create.CommandText = "SELECT 1 FROM pg_database WHERE datname = 'testdb_tenant_database_requests'";
            if (await create.ExecuteScalarAsync() is null)
            {
                create.CommandText = "CREATE DATABASE testdb_tenant_database_requests";
                await create.ExecuteNonQueryAsync();
            }
        }

        await using var dataSource = NpgsqlDataSource.Create(connectionString);
        await using (var connection = await dataSource.OpenConnectionAsync())
        await using (var migration = connection.CreateCommand())
        {
            migration.CommandText = await File.ReadAllTextAsync(Path.Combine(AppContext.BaseDirectory,
                "tenant-database", "001_create_tenant_database_requests.sql"));
            await migration.ExecuteNonQueryAsync();
        }

        var store = new PostgresTenantDatabaseRequestStore(dataSource);
        using var privateKey = RSA.Create(2048);
        using var keys = new SubscriptionPaymentVerificationKeys([privateKey.ExportSubjectPublicKeyInfoPem()]);
        var intake = new SignedTenantDatabaseIntake(store, keys);
        var now = DateTimeOffset.UtcNow;
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SourceOrderId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            TenantIdentifier = "real-postgres-test-tenant", PaymentMethod = "captured", DatabaseStrategy = "dedicated",
            DatabaseProvider = "postgres", PlanId = "Business", PlanVersion = "2026-10", AmountMinor = 10000,
            Currency = "USD", ReplicaCount = 1, HasReadReplicas = true, ConfirmedAtUtc = now,
            ValidFromUtc = now, ValidUntilUtc = now.AddDays(365),
        };
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);

        await intake.Handle(paid, CancellationToken.None);
        await intake.Handle(paid, CancellationToken.None);
        var requests = await store.GetPendingAsync(32, CancellationToken.None);
        var recorded = Assert.Single(requests, request => request.SubscriptionId == paid.SubscriptionId);
        Assert.Equal(paid.SourceOrderId, recorded.SourceOrderId);
        Assert.Equal(paid.Proof, recorded.Proof);
        Assert.Equal(paid.TenantId.ToString("D"), recorded.DesiredState.TenantId);

        var firstOrder = paid.SourceOrderId;
        var nextOrder = Guid.NewGuid();
        paid.PreviousSourceOrderId = firstOrder;
        paid.SourceOrderId = nextOrder;
        paid.ConfirmedAtUtc = now.AddDays(30);
        paid.ValidFromUtc = now.AddDays(365);
        paid.ValidUntilUtc = paid.ValidFromUtc.AddYears(1);
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);
        await intake.Handle(paid, CancellationToken.None);
        await intake.Handle(paid, CancellationToken.None);
        var renewed = await store.GetPendingAsync(32, CancellationToken.None);
        var renewedRecord = Assert.Single(renewed, request => request.SubscriptionId == paid.SubscriptionId);
        Assert.Equal(nextOrder, renewedRecord.SourceOrderId);

        var staleSuspension = new SubscriptionAccessSuspendedIntegrationEvent
        {
            SourceOrderId = firstOrder, SubscriptionId = paid.SubscriptionId, TenantId = paid.TenantId,
            TenantIdentifier = paid.TenantIdentifier, Reason = "renewal_failed", SuspendedAtUtc = now.AddDays(365),
        };
        staleSuspension.Proof = SubscriptionSuspensionProof.Sign(staleSuspension, privateKey);
        await new SignedTenantDatabaseSuspensionIntake(store, keys).Handle(staleSuspension, CancellationToken.None);
        Assert.Equal(TenantDatabasePhase.Pending, (await store.GetStatusAsync(paid.SubscriptionId, CancellationToken.None))!.Phase);

        var currentSuspension = new SubscriptionAccessSuspendedIntegrationEvent
        {
            SourceOrderId = nextOrder, SubscriptionId = paid.SubscriptionId, TenantId = paid.TenantId,
            TenantIdentifier = paid.TenantIdentifier, Reason = "cancelled", SuspendedAtUtc = now.AddDays(31),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => new SignedTenantDatabaseSuspensionIntake(store, keys)
            .Handle(currentSuspension, CancellationToken.None));
        currentSuspension.Proof = SubscriptionSuspensionProof.Sign(currentSuspension, privateKey);
        await new SignedTenantDatabaseSuspensionIntake(store, keys).Handle(currentSuspension, CancellationToken.None);
        Assert.Equal(TenantSubscriptionStatus.Suspended,
            Assert.Single(await store.GetPendingAsync(32, CancellationToken.None), request => request.SubscriptionId == paid.SubscriptionId)
                .DesiredState.SubscriptionStatus);

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveStatusAsync(renewedRecord,
            new TenantDatabaseStatus(paid.TenantId.ToString("D"), TenantDatabasePhase.Ready),
            DateTimeOffset.UtcNow, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() => store.SaveStatusAsync(recorded,
            new TenantDatabaseStatus(paid.TenantId.ToString("D"), TenantDatabasePhase.Ready),
            DateTimeOffset.UtcNow, CancellationToken.None));
        Assert.Equal(TenantDatabasePhase.Pending, (await store.GetStatusAsync(paid.SubscriptionId, CancellationToken.None))!.Phase);

        paid.PreviousSourceOrderId = Guid.NewGuid();
        paid.SourceOrderId = Guid.NewGuid();
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => intake.Handle(paid, CancellationToken.None));

        paid.PreviousSourceOrderId = Guid.Empty;
        paid.SourceOrderId = firstOrder;
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => intake.Handle(paid, CancellationToken.None));
        await using var verification = await dataSource.OpenConnectionAsync();
        await using var query = verification.CreateCommand();
        query.CommandText = "SELECT source_order_id FROM tenant_database_requests WHERE subscription_id = @subscription";
        query.Parameters.AddWithValue("subscription", paid.SubscriptionId);
        Assert.Equal(nextOrder, (Guid)(await query.ExecuteScalarAsync())!);
    }
}
