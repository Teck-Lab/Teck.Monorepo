using Licensing.Application.Database;
using Microsoft.EntityFrameworkCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Tests subscription-level cancellation persistence against migrated PostgreSQL.</summary>
[Collection("SharedTestcontainers")]
public sealed class SubscriptionLicenseSuspensionTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task Cancellation_before_or_after_payment_prevents_new_active_license_and_revokes_existing()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(LicensingDbContext), "Licensing.Host");
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsAssembly("Licensing.Host")).Options;
        var subscriptionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await using (var cancelledDb = new LicensingDbContext(options))
        {
            var cancelledStore = new LicenseDbStore(cancelledDb);
            await cancelledStore.SuspendAsync(orderId, subscriptionId, tenantId, now, CancellationToken.None);
            await cancelledStore.SuspendAsync(orderId, subscriptionId, tenantId, now, CancellationToken.None);
        }

        await using (var delayedDb = new LicensingDbContext(options))
        {
            var delayedStore = new LicenseDbStore(delayedDb);
            Assert.True(await delayedStore.IsSuspendedAsync(orderId, subscriptionId, CancellationToken.None));
            await Assert.ThrowsAsync<InvalidOperationException>(() => delayedStore.AddUnlessSuspendedAsync(
                NewLicense(orderId, subscriptionId, tenantId, now), CancellationToken.None));
            Assert.False(await delayedDb.Licenses.AnyAsync(row => row.SubscriptionId == subscriptionId));
        }

        var activeSubscription = Guid.NewGuid();
        var activeOrderId = Guid.NewGuid();
        await using (var paidDb = new LicensingDbContext(options))
        {
            await new LicenseDbStore(paidDb).AddUnlessSuspendedAsync(
                NewLicense(activeOrderId, activeSubscription, tenantId, now), CancellationToken.None);
        }

        await using (var suspendedDb = new LicensingDbContext(options))
        {
            await new LicenseDbStore(suspendedDb).SuspendAsync(activeOrderId, activeSubscription, tenantId, now, CancellationToken.None);
        }

        await using (var verifiedDb = new LicensingDbContext(options))
        {
            Assert.True((await verifiedDb.Licenses.SingleAsync(row => row.SubscriptionId == activeSubscription)).Revoked);
            Assert.True(await verifiedDb.SubscriptionSuspensions.AnyAsync(row => row.SubscriptionId == activeSubscription));
            await Assert.ThrowsAsync<InvalidOperationException>(() => new LicenseDbStore(verifiedDb).AddUnlessSuspendedAsync(
                NewLicense(activeOrderId, activeSubscription, tenantId, now), CancellationToken.None));
        }
    }

    [Fact]
    public async Task Concurrent_issue_and_suspension_leave_no_active_license()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(LicensingDbContext), "Licensing.Host");
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsAssembly("Licensing.Host")).Options;
        var subscriptionId = Guid.NewGuid();
        var orderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        using var start = new ManualResetEventSlim();
        var issue = Task.Run(async () =>
        {
            await using var db = new LicensingDbContext(options);
            start.Wait();
            try
            {
                await new LicenseDbStore(db).AddUnlessSuspendedAsync(
                    NewLicense(orderId, subscriptionId, tenantId, now), CancellationToken.None);
            }
            catch (InvalidOperationException)
            {
                // Cancellation won the race; the issue path must fail closed.
            }
        });
        var suspend = Task.Run(async () =>
        {
            await using var db = new LicensingDbContext(options);
            start.Wait();
            await new LicenseDbStore(db).SuspendAsync(orderId, subscriptionId, tenantId, now, CancellationToken.None);
        });
        start.Set();
        await Task.WhenAll(issue, suspend);

        await using var verifiedDb = new LicensingDbContext(options);
        Assert.True(await verifiedDb.SubscriptionSuspensions.AnyAsync(row => row.SubscriptionId == subscriptionId));
        Assert.False(await verifiedDb.Licenses.AnyAsync(row => row.SubscriptionId == subscriptionId && !row.Revoked));
    }

    [Fact]
    public async Task Late_renewal_replacement_cannot_reactivate_cancelled_subscription()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(LicensingDbContext), "Licensing.Host");
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsAssembly("Licensing.Host")).Options;
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var initial = NewLicense(orderId, subscriptionId, tenantId, now);
        await using (var issuedDb = new LicensingDbContext(options))
        {
            await new LicenseDbStore(issuedDb).AddUnlessSuspendedAsync(initial, CancellationToken.None);
        }

        await using (var cancelledDb = new LicensingDbContext(options))
        {
            await new LicenseDbStore(cancelledDb).SuspendAsync(orderId, subscriptionId, tenantId, now.AddDays(1), CancellationToken.None);
        }

        await using (var renewedDb = new LicensingDbContext(options))
        {
            await Assert.ThrowsAsync<InvalidOperationException>(() => new LicenseDbStore(renewedDb).ReplaceAsync(
                initial.LicenseId, NewLicense(orderId, subscriptionId, tenantId, now.AddDays(1)), CancellationToken.None));
        }

        await using var verifiedDb = new LicensingDbContext(options);
        Assert.False(await verifiedDb.Licenses.AnyAsync(row => row.SubscriptionId == subscriptionId && !row.Revoked));
    }

    [Fact]
    public async Task NewPaidOrder_ReplacesActiveLicense_ThenOldOrderSuspensionCannotRevokeSuccessor()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(LicensingDbContext), "Licensing.Host");
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsAssembly("Licensing.Host")).Options;
        var subscription = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var oldOrder = Guid.NewGuid();
        var newOrder = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var first = NewLicense(oldOrder, subscription, tenant, now);
        var renewal = NewLicense(newOrder, subscription, tenant, now.AddDays(20));
        await using (var db = new LicensingDbContext(options))
            await new LicenseDbStore(db).AddUnlessSuspendedAsync(first, CancellationToken.None);

        await using (var db = new LicensingDbContext(options))
            await new LicenseDbStore(db).ReplaceAsync(first.LicenseId, renewal, CancellationToken.None);

        await using (var db = new LicensingDbContext(options))
            await new LicenseDbStore(db).SuspendAsync(oldOrder, subscription, tenant, now.AddDays(30), CancellationToken.None);

        await using var verify = new LicensingDbContext(options);
        Assert.True((await verify.Licenses.SingleAsync(row => row.Id == first.LicenseId)).Revoked);
        Assert.False((await verify.Licenses.SingleAsync(row => row.Id == renewal.LicenseId)).Revoked);
        Assert.True(await verify.SubscriptionSuspensions.AnyAsync(row => row.SourceOrderId == oldOrder));
        Assert.False(await new LicenseDbStore(verify).IsSuspendedAsync(newOrder, subscription, CancellationToken.None));
    }

    [Fact]
    public async Task PreviouslySuspendedPaidOrder_CanBeReplacedByFreshPeriodWithoutReopeningItsLicense()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(LicensingDbContext), "Licensing.Host");
        var options = new DbContextOptionsBuilder<LicensingDbContext>()
            .UseNpgsql(connectionString, postgres => postgres.MigrationsAssembly("Licensing.Host")).Options;
        var oldOrder = Guid.NewGuid();
        var newOrder = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var tenant = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var original = NewLicense(oldOrder, subscription, tenant, now.AddYears(-1));
        var recovery = NewLicense(newOrder, subscription, tenant, now);
        await using (var db = new LicensingDbContext(options))
            await new LicenseDbStore(db).AddUnlessSuspendedAsync(original, CancellationToken.None);
        await using (var db = new LicensingDbContext(options))
            await new LicenseDbStore(db).SuspendAsync(oldOrder, subscription, tenant, now, CancellationToken.None);
        await using (var db = new LicensingDbContext(options))
            await new LicenseDbStore(db).ReplaceAsync(original.LicenseId, recovery, CancellationToken.None);

        await using var verify = new LicensingDbContext(options);
        Assert.True((await verify.Licenses.SingleAsync(row => row.Id == original.LicenseId)).Revoked);
        Assert.False((await verify.Licenses.SingleAsync(row => row.Id == recovery.LicenseId)).Revoked);
        Assert.True(await new LicenseDbStore(verify).IsSuspendedAsync(oldOrder, subscription, CancellationToken.None));
        Assert.False(await new LicenseDbStore(verify).IsSuspendedAsync(newOrder, subscription, CancellationToken.None));
    }


    private static Licensing.Application.IssuedLicense NewLicense(Guid orderId, Guid subscriptionId, Guid tenantId, DateTimeOffset now) =>
        new(Guid.NewGuid(), orderId, subscriptionId, tenantId, "Business", "v1", now, now.AddDays(30), "test-only-signed-document", "test-key", false);
}
