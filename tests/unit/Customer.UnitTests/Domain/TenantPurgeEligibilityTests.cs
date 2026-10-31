using Customers.Domain.Entities;
using Xunit;

namespace Customer.UnitTests.Domain;

public sealed class TenantPurgeEligibilityTests
{
    [Fact]
    public void IsPurgeEligibleAt_RequiresSuspendedPaidDedicatedTenantAndElapsedRecoveryWindow()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), "eligible-tenant", "buyer-subject");
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var suspendedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        tenant.ApplyPurchaseConfirmation(orderId, subscriptionId, "Business", "2026-10", "dedicated", "postgres", false,
            suspendedAt.AddYears(1));
        tenant.SuspendSubscription(orderId, subscriptionId, suspendedAt);

        Assert.False(tenant.IsPurgeEligibleAt(suspendedAt.AddDays(29), TimeSpan.FromDays(30)));
        Assert.True(tenant.IsPurgeEligibleAt(suspendedAt.AddDays(30), TimeSpan.FromDays(30)));
        Assert.False(tenant.IsPurgeEligibleAt(suspendedAt.AddDays(30), TimeSpan.Zero));
    }

    [Fact]
    public void IsPurgeEligibleAt_RejectsPendingSharedAndRecoveredTenant()
    {
        var pending = Tenant.CreatePendingReservation(Guid.NewGuid(), "pending-tenant", "buyer-subject");
        var now = new DateTimeOffset(2026, 11, 1, 0, 0, 0, TimeSpan.Zero);
        Assert.False(pending.IsPurgeEligibleAt(now, TimeSpan.FromDays(30)));

        var shared = Tenant.CreatePendingReservation(Guid.NewGuid(), "shared-tenant", "buyer-subject");
        var sharedOrder = Guid.NewGuid();
        var sharedSubscription = Guid.NewGuid();
        shared.ApplyPurchaseConfirmation(sharedOrder, sharedSubscription, "Business", "2026-10", "shared", "postgres", false,
            now.AddYears(1));
        shared.SuspendSubscription(sharedOrder, sharedSubscription, now.AddDays(-31));
        Assert.False(shared.IsPurgeEligibleAt(now, TimeSpan.FromDays(30)));

        var recovered = Tenant.CreatePendingReservation(Guid.NewGuid(), "recovered-tenant", "buyer-subject");
        var paidOrder = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        recovered.ApplyPurchaseConfirmation(paidOrder, subscription, "Business", "2026-10", "dedicated", "postgres", false,
            now.AddYears(1));
        recovered.LinkOrganization(Guid.NewGuid());
        recovered.SuspendSubscription(paidOrder, subscription, now.AddDays(-31));
        recovered.ApplyRenewalConfirmation(paidOrder, Guid.NewGuid(), subscription, "Business", "2026-10", "dedicated", "postgres", false,
            now.AddYears(2));
        Assert.False(recovered.IsPurgeEligibleAt(now, TimeSpan.FromDays(30)));
    }
}
