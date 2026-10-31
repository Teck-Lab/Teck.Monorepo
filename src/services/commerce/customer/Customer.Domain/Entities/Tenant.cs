using SharedKernel.Core.Domain;

namespace Customers.Domain.Entities;

/// <summary>The global tenant registry record and authority for per-tenant database strategy.</summary>
public sealed class Tenant : BaseEntity, IAggregateRoot
{
    private Tenant(Guid id, string identifier, string databaseStrategy, string databaseProvider, bool hasReadReplicas, string ownerSubjectId)
    {
        Id = id;
        Identifier = identifier;
        DatabaseStrategy = databaseStrategy;
        DatabaseProvider = databaseProvider;
        HasReadReplicas = hasReadReplicas;
        OwnerSubjectId = ownerSubjectId;
        Status = "pending";
    }

    private Tenant()
    {
    }

    /// <summary>Gets the tenant's unique identifier slug.</summary>
    public string Identifier { get; private set; } = string.Empty;

    /// <summary>Gets the tenant's database strategy.</summary>
    public string DatabaseStrategy { get; private set; } = string.Empty;

    /// <summary>Gets the tenant's database provider.</summary>
    public string DatabaseProvider { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the tenant has read replicas.</summary>
    public bool HasReadReplicas { get; private set; }

    /// <summary>Gets the Keycloak subject that owns the pending reservation.</summary>
    public string OwnerSubjectId { get; private set; } = string.Empty;

    /// <summary>Gets the selected subscription plan identity.</summary>
    public string PlanId { get; private set; } = string.Empty;

    /// <summary>Gets the selected immutable subscription plan version.</summary>
    public string PlanVersion { get; private set; } = string.Empty;

    /// <summary>Gets the correlated purchase order identifier.</summary>
    public Guid PurchaseOrderId { get; private set; }

    /// <summary>Gets the prior paid source order for replay protection of the current renewal.</summary>
    public Guid PreviousPaidOrderId { get; private set; }

    /// <summary>Gets the confirmed subscription used to correlate downstream readiness.</summary>
    public Guid SubscriptionId { get; private set; }

    /// <summary>Gets the linked Keycloak organization identifier after verified payment.</summary>
    public Guid? KeycloakOrganizationId { get; private set; }

    /// <summary>Gets the tenant status.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Gets whether license provisioning is ready.</summary>
    public bool LicenseReady { get; private set; }

    /// <summary>Gets whether database provisioning is ready.</summary>
    public bool DatabaseReady { get; private set; }

    /// <summary>Gets the end of the currently verified paid period, exclusive.</summary>
    public DateTimeOffset? ValidUntilUtc { get; private set; }

    /// <summary>Gets the effective time at which the subscription lost tenant access.</summary>
    public DateTimeOffset? SuspendedAtUtc { get; private set; }

    /// <summary>Creates a pending tenant reservation bound to its owner.</summary>
    /// <param name="id">Generated tenant identifier.</param>
    /// <param name="identifier">Generated tenant slug.</param>
    /// <param name="ownerSubjectId">Authenticated Keycloak subject.</param>
    /// <returns>The pending reservation.</returns>
    public static Tenant CreatePendingReservation(Guid id, string identifier, string ownerSubjectId)
    {
        if (string.IsNullOrWhiteSpace(identifier) || string.IsNullOrWhiteSpace(ownerSubjectId))
        {
            throw new ArgumentException("Tenant reservation identity is incomplete.");
        }

        return new Tenant(id, identifier, string.Empty, string.Empty, false, ownerSubjectId);
    }

    /// <summary>Creates a new active tenant registry record.</summary>
    /// <param name="id">The tenant identifier.</param>
    /// <param name="identifier">The tenant slug.</param>
    /// <param name="databaseStrategy">The database strategy.</param>
    /// <param name="databaseProvider">The database provider.</param>
    /// <param name="hasReadReplicas">Whether replicas are configured.</param>
    /// <returns>The tenant record.</returns>
    public static Tenant Create(Guid id, string identifier, string databaseStrategy, string databaseProvider, bool hasReadReplicas)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier must be provided.", nameof(identifier));
        }

        var tenant = new Tenant(id, identifier, databaseStrategy, databaseProvider, hasReadReplicas, string.Empty);
        tenant.KeycloakOrganizationId = id;
        tenant.Status = "active";
        return tenant;
    }

    /// <summary>Applies immutable purchase attributes from a trusted Billing confirmation event.</summary>
    /// <param name="purchaseOrderId">Confirmed Billing order identifier.</param>
    /// <param name="subscriptionId">Billing-confirmed subscription identifier.</param>
    /// <param name="planId">Server-owned plan identifier.</param>
    /// <param name="planVersion">Server-owned plan version.</param>
    /// <param name="databaseStrategy">Quoted database strategy.</param>
    /// <param name="databaseProvider">Quoted database provider.</param>
    /// <param name="hasReadReplicas">Whether quote includes replicas.</param>
    /// <param name="validUntilUtc">Exclusive end of the paid period attested by Billing.</param>
    public void ApplyPurchaseConfirmation(Guid purchaseOrderId, Guid subscriptionId, string planId, string planVersion, string databaseStrategy, string databaseProvider, bool hasReadReplicas, DateTimeOffset validUntilUtc)
    {
        if (Status != "pending" || purchaseOrderId == Guid.Empty || subscriptionId == Guid.Empty || string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(planVersion) || validUntilUtc == default)
        {
            throw new InvalidOperationException("Only a pending tenant can accept a valid purchase confirmation.");
        }

        if (PurchaseOrderId != Guid.Empty && PurchaseOrderId != purchaseOrderId)
        {
            throw new InvalidOperationException("Tenant is already correlated with another purchase order.");
        }

        PurchaseOrderId = purchaseOrderId;
        SubscriptionId = subscriptionId;
        PlanId = planId;
        PlanVersion = planVersion;
        DatabaseStrategy = databaseStrategy;
        DatabaseProvider = databaseProvider;
        ValidUntilUtc = validUntilUtc;
        Status = "paid_pending_readiness";
    }

    /// <summary>Accepts a verified next paid order only after its preceding order became the current tenant period.</summary>
    /// <param name="previousOrderId">Currently accepted source order ID.</param>
    /// <param name="orderId">Distinct paid renewal order ID.</param>
    /// <param name="subscriptionId">Stable subscription ID across paid periods.</param>
    /// <param name="planId">Server-owned plan identifier.</param>
    /// <param name="planVersion">Server-owned plan version.</param>
    /// <param name="databaseStrategy">Quoted tenant database strategy.</param>
    /// <param name="databaseProvider">Quoted tenant database provider.</param>
    /// <param name="hasReadReplicas">Whether the renewed quote includes replicas.</param>
    /// <param name="validUntilUtc">Exclusive end of the new paid period.</param>
    public void ApplyRenewalConfirmation(Guid previousOrderId, Guid orderId, Guid subscriptionId,
        string planId, string planVersion, string databaseStrategy, string databaseProvider,
        bool hasReadReplicas, DateTimeOffset validUntilUtc)
    {
        if (PurchaseOrderId == Guid.Empty || PurchaseOrderId != previousOrderId ||
            orderId == Guid.Empty || orderId == previousOrderId || SubscriptionId != subscriptionId ||
            ValidUntilUtc is not { } oldExpiry || validUntilUtc <= oldExpiry ||
            Status is not ("active" or "suspended" or "paid_pending_readiness") ||
            string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(planVersion) ||
            KeycloakOrganizationId is null)
        {
            throw new InvalidOperationException("A renewal must extend the current verified paid tenant and subscription.");
        }

        PreviousPaidOrderId = previousOrderId;
        PurchaseOrderId = orderId;
        PlanId = planId;
        PlanVersion = planVersion;
        DatabaseStrategy = databaseStrategy;
        DatabaseProvider = databaseProvider;
        HasReadReplicas = hasReadReplicas;
        ValidUntilUtc = validUntilUtc;
        LicenseReady = false;
        DatabaseReady = false;
        SuspendedAtUtc = null;
        Status = "paid_pending_readiness";
    }

    /// <summary>Marks readiness for a downstream licensing or database provisioner.</summary>
    /// <param name="provisioner">The readiness authority, either <c>license</c> or <c>database</c>.</param>
    public void MarkProvisionerReady(string provisioner)
    {
        if (Status == "active" &&
            (provisioner == "license" && LicenseReady || provisioner == "database" && DatabaseReady))
        {
            return;
        }

        if (Status != "paid_pending_readiness")
        {
            throw new InvalidOperationException("Only a paid tenant awaiting readiness can receive readiness updates.");
        }

        if (string.Equals(provisioner, "license", StringComparison.Ordinal))
        {
            LicenseReady = true;
        }
        else if (string.Equals(provisioner, "database", StringComparison.Ordinal))
        {
            DatabaseReady = true;
        }
        else
        {
            throw new ArgumentException("Readiness authority must be license or database.", nameof(provisioner));
        }

        ActivateIfReady();
    }

    /// <summary>Links the paid reservation to the verified Keycloak organization.</summary>
    /// <param name="organizationId">Keycloak-generated organization identifier.</param>
    public void LinkOrganization(Guid organizationId)
    {
        if (Status != "paid_pending_readiness" || organizationId == Guid.Empty ||
            (KeycloakOrganizationId.HasValue && KeycloakOrganizationId != organizationId))
        {
            throw new InvalidOperationException("Only the paid tenant may link its original Keycloak organization.");
        }

        KeycloakOrganizationId = organizationId;
        ActivateIfReady();
    }

    /// <summary>Suspends access for the authoritative order and subscription, including out-of-order cancellation.</summary>
    /// <param name="purchaseOrderId">The original paid purchase order.</param>
    /// <param name="subscriptionId">The subscription whose entitlement is being suspended.</param>
    /// <param name="suspendedAtUtc">Billing's immutable effective suspension time.</param>
    public void SuspendSubscription(Guid purchaseOrderId, Guid subscriptionId, DateTimeOffset suspendedAtUtc)
    {
        if (purchaseOrderId == Guid.Empty || subscriptionId == Guid.Empty || suspendedAtUtc == default ||
            PurchaseOrderId != Guid.Empty && PurchaseOrderId != purchaseOrderId ||
            SubscriptionId != Guid.Empty && SubscriptionId != subscriptionId)
        {
            throw new InvalidOperationException("Suspension must match the original paid subscription.");
        }

        if (Status == "suspended")
        {
            // PostgreSQL stores timestamptz at microsecond precision; broker timestamps retain 100-ns ticks.
            if (SuspendedAtUtc is not { } recorded ||
                Math.Abs((recorded - suspendedAtUtc).Ticks) >= TimeSpan.TicksPerMicrosecond)
            {
                throw new InvalidOperationException("Subscription suspension time is immutable.");
            }

            return;
        }

        if (Status is not ("pending" or "paid_pending_readiness" or "active") ||
            Status == "active" && PurchaseOrderId == Guid.Empty)
        {
            throw new InvalidOperationException("Only a paid tenant or its pending reservation can be suspended.");
        }

        PurchaseOrderId = purchaseOrderId;
        SubscriptionId = subscriptionId;
        LicenseReady = false;
        DatabaseReady = false;
        SuspendedAtUtc = suspendedAtUtc;
        Status = "suspended";
    }

    /// <summary>Checks whether the current dedicated paid period has remained suspended through recovery retention.</summary>
    /// <param name="now">Trusted UTC decision time.</param>
    /// <param name="recoveryWindow">Positive configured recovery duration.</param>
    /// <returns>True only after the current paid dedicated PostgreSQL tenant has completed retention.</returns>
    public bool IsPurgeEligibleAt(DateTimeOffset now, TimeSpan recoveryWindow) =>
        recoveryWindow > TimeSpan.Zero && now.Offset == TimeSpan.Zero &&
        Status == "suspended" && PurchaseOrderId != Guid.Empty && SubscriptionId != Guid.Empty &&
        string.Equals(DatabaseStrategy, "dedicated", StringComparison.OrdinalIgnoreCase) &&
        string.Equals(DatabaseProvider, "postgres", StringComparison.OrdinalIgnoreCase) &&
        SuspendedAtUtc is { } suspendedAt && now >= suspendedAt + recoveryWindow;

    private void ActivateIfReady()
    {
        if (KeycloakOrganizationId.HasValue && LicenseReady && DatabaseReady)
        {
            Status = "active";
        }
    }
}
