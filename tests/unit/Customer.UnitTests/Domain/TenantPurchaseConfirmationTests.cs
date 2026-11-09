using Customers.Domain.Entities;
using Xunit;

namespace Customer.UnitTests.Domain;

public sealed class TenantPurchaseConfirmationTests
{
    [Fact]
    public void ApplyPurchaseConfirmation_ActivatesOnlyAfterLicenseAndDatabaseReadiness()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), "acme-owner", "owner");
        var orderId = Guid.NewGuid();

        tenant.ApplyPurchaseConfirmation(orderId, Guid.NewGuid(), "Business", "2026-10", "shared", "postgres", false, DateTimeOffset.UtcNow.AddYears(1));
        Assert.Equal("paid_pending_readiness", tenant.Status);
        Assert.True(tenant.ValidUntilUtc > DateTimeOffset.UtcNow);

        tenant.MarkProvisionerReady("license");
        Assert.Equal("paid_pending_readiness", tenant.Status);
        tenant.MarkProvisionerReady("database");
        Assert.Equal("paid_pending_readiness", tenant.Status);
        tenant.LinkOrganization(Guid.NewGuid());
        Assert.Equal("active", tenant.Status);
    }

    [Fact]
    public void SuspendSubscription_RevokesActiveReadinessAndCannotBeReopenedByDelayedEvents()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), "paid-business", "owner");
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        tenant.ApplyPurchaseConfirmation(orderId, subscriptionId, "Business", "2026-10", "shared", "postgres", false, DateTimeOffset.UtcNow.AddYears(1));
        tenant.MarkProvisionerReady("license");
        tenant.MarkProvisionerReady("database");
        tenant.LinkOrganization(Guid.NewGuid());
        Assert.Equal("active", tenant.Status);

        var cancelledAt = DateTimeOffset.UtcNow;
        tenant.SuspendSubscription(orderId, subscriptionId, cancelledAt);

        Assert.Equal("suspended", tenant.Status);
        Assert.Equal(cancelledAt, tenant.SuspendedAtUtc);
        Assert.False(tenant.LicenseReady);
        Assert.False(tenant.DatabaseReady);
        tenant.SuspendSubscription(orderId, subscriptionId, cancelledAt);
        Assert.Throws<InvalidOperationException>(() => tenant.MarkProvisionerReady("license"));
        Assert.Throws<InvalidOperationException>(() => tenant.ApplyPurchaseConfirmation(orderId, subscriptionId,
            "Business", "2026-10", "shared", "postgres", false, DateTimeOffset.UtcNow.AddYears(1)));
        Assert.Throws<InvalidOperationException>(() => tenant.SuspendSubscription(Guid.NewGuid(), subscriptionId, cancelledAt));
    }

    [Fact]
    public void RenewPurchase_DoesNotReuseOldPeriodReadinessOrRecreateOwnerOrganization()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), "renewal-business", "owner");
        var oldOrder = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var organization = Guid.NewGuid();
        var oldExpiry = DateTimeOffset.UtcNow.AddDays(20);
        tenant.ApplyPurchaseConfirmation(oldOrder, subscription, "Business", "2026-10", "dedicated", "postgres", false, oldExpiry);
        tenant.LinkOrganization(organization);
        tenant.MarkProvisionerReady("license");
        tenant.MarkProvisionerReady("database");

        var renewal = Guid.NewGuid();
        tenant.ApplyRenewalConfirmation(oldOrder, renewal, subscription, "Business", "2026-10",
            "dedicated", "postgres", false, oldExpiry.AddYears(1));
        Assert.Equal("paid_pending_readiness", tenant.Status);
        Assert.Equal(organization, tenant.KeycloakOrganizationId);
        Assert.Equal(renewal, tenant.PurchaseOrderId);
        Assert.False(tenant.LicenseReady);
        Assert.False(tenant.DatabaseReady);
        tenant.MarkProvisionerReady("license");
        Assert.Equal("paid_pending_readiness", tenant.Status);
        tenant.MarkProvisionerReady("database");
        Assert.Equal("active", tenant.Status);
        Assert.Equal(oldExpiry.AddYears(1), tenant.ValidUntilUtc);
        Assert.Throws<InvalidOperationException>(() => tenant.ApplyRenewalConfirmation(oldOrder, Guid.NewGuid(),
            subscription, "Business", "2026-10", "dedicated", "postgres", false, oldExpiry.AddYears(2)));
    }

    [Fact]
    public void SuspendSubscription_BeforeDelayedPaidDeliveryPreventsProvisioning()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), "unprocessed-business", "owner");
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        tenant.SuspendSubscription(orderId, subscriptionId, DateTimeOffset.UtcNow);

        Assert.Equal("suspended", tenant.Status);
        Assert.Equal(orderId, tenant.PurchaseOrderId);
        Assert.Equal(subscriptionId, tenant.SubscriptionId);
        Assert.Throws<InvalidOperationException>(() => tenant.ApplyPurchaseConfirmation(orderId, subscriptionId,
            "Business", "2026-10", "dedicated", "postgres", false, DateTimeOffset.UtcNow.AddYears(1)));
    }

    [Fact]
    public void MarkProvisionerReady_RejectsUnknownAuthority()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), "acme-owner", "owner");
        tenant.ApplyPurchaseConfirmation(Guid.NewGuid(), Guid.NewGuid(), "Business", "2026-10", "shared", "postgres", false, DateTimeOffset.UtcNow.AddYears(1));

        Assert.Throws<ArgumentException>(() => tenant.MarkProvisionerReady("unknown"));
        Assert.Equal("paid_pending_readiness", tenant.Status);
    }

    [Fact]
    public void LinkOrganization_RejectsConflictingKeycloakIdentity()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), "acme-owner", "owner");
        Assert.Throws<InvalidOperationException>(() => tenant.LinkOrganization(Guid.NewGuid()));
        tenant.ApplyPurchaseConfirmation(Guid.NewGuid(), Guid.NewGuid(), "Business", "2026-10", "shared", "postgres", false, DateTimeOffset.UtcNow.AddYears(1));
        var organizationId = Guid.NewGuid();
        tenant.LinkOrganization(organizationId);
        tenant.LinkOrganization(organizationId);
        Assert.Throws<InvalidOperationException>(() => tenant.LinkOrganization(Guid.NewGuid()));
    }
}
