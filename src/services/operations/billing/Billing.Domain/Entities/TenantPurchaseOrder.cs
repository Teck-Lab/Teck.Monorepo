using Billings.Domain.DomainEvents;
using SharedKernel.Core.Domain;
using SharedKernel.Core.Pricing;

namespace Billings.Domain.Entities;

/// <summary>Durable immutable quote and payment lifecycle for one tenant purchase.</summary>
public sealed class TenantPurchaseOrder : BaseEntity, IAggregateRoot
{
    private TenantPurchaseOrder() { }

    /// <summary>Gets the tenant identifier slug.</summary>
    public string TenantIdentifier { get; private set; } = string.Empty;

    /// <summary>Gets the global tenant registry identifier.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the confirmed subscription identifier.</summary>
    public Guid SubscriptionId { get; private set; }
    /// <summary>Gets the prior independently paid period, or empty for the original purchase.</summary>
    public Guid PreviousPurchaseOrderId { get; private set; }

    /// <summary>Gets the immutable plan identifier.</summary>
    public string PlanId { get; private set; } = string.Empty;

    /// <summary>Gets the immutable plan version.</summary>
    public string PlanVersion { get; private set; } = string.Empty;

    /// <summary>Gets the order total in minor currency units.</summary>
    public long AmountMinor { get; private set; }

    /// <summary>Gets the ISO currency code.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Gets the database strategy snapshot.</summary>
    public string DatabaseStrategy { get; private set; } = string.Empty;

    /// <summary>Gets the database provider snapshot.</summary>
    public string DatabaseProvider { get; private set; } = string.Empty;

    /// <summary>Gets whether read replicas were included.</summary>
    public bool HasReadReplicas { get; private set; }

    /// <summary>Gets the read replica count.</summary>
    public int ReplicaCount { get; private set; }

    /// <summary>Gets the order payment lifecycle status.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Gets the confirmed payment method.</summary>
    public string PaymentMethod { get; private set; } = string.Empty;

    /// <summary>Gets the audited external invoice reference.</summary>
    public string ExternalInvoiceReference { get; private set; } = string.Empty;

    /// <summary>Gets the authenticated invoice verifier.</summary>
    public string InvoiceVerifiedBy { get; private set; } = string.Empty;

    /// <summary>Gets the authenticated subject who created this order.</summary>
    public string CreatedBySubjectId { get; private set; } = string.Empty;

    /// <summary>Gets the verified reservation owner for this purchase.</summary>
    public string BeneficiarySubjectId { get; private set; } = string.Empty;

    /// <summary>Gets the Stripe PaymentIntent bound to this pending order.</summary>
    public string? StripePaymentIntentId { get; private set; }

    /// <summary>Gets the creation timestamp.</summary>
    public DateTimeOffset CreatedAtUtc { get; private set; }

    /// <summary>Gets the confirmation timestamp.</summary>
    public DateTimeOffset? ConfirmedAtUtc { get; private set; }

    /// <summary>Gets the exclusive start of this paid annual period.</summary>
    public DateTimeOffset? ValidFromUtc { get; private set; }

    /// <summary>Gets the subscription expiry timestamp.</summary>
    public DateTimeOffset? ValidUntilUtc { get; private set; }

    /// <summary>Gets the cancellation time used to suspend tenant access and start retention.</summary>
    public DateTimeOffset? CancelledAtUtc { get; private set; }

    /// <summary>Creates a durable quote using the server-owned plan price and configured add-on rates.</summary>
    /// <param name="tenantId">The global tenant registry identifier.</param>
    /// <param name="tenantIdentifier">The tenant identifier slug.</param>
    /// <param name="plan">The server-selected plan and base price.</param>
    /// <param name="planVersion">The immutable plan version.</param>
    /// <param name="databaseStrategy">The database strategy to snapshot.</param>
    /// <param name="databaseProvider">The database provider to snapshot.</param>
    /// <param name="hasReadReplicas">Whether to include read replicas.</param>
    /// <param name="replicaCount">The requested number of replicas.</param>
    /// <param name="monthlyAddonRates">The server-configured add-on rates.</param>
    /// <param name="createdAtUtc">The quote creation timestamp.</param>
    /// <param name="actorSubjectId">The authenticated quote creator.</param>
    /// <param name="beneficiarySubjectId">The Customer-verified reservation owner.</param>
    /// <returns>The new pending purchase order with a server-calculated total.</returns>
    public static TenantPurchaseOrder Create(
        Guid tenantId,
        string tenantIdentifier,
        TenantPlan plan,
        string planVersion,
        DatabaseStrategy databaseStrategy,
        string databaseProvider,
        bool hasReadReplicas,
        int replicaCount,
        IReadOnlyDictionary<string, decimal> monthlyAddonRates,
        DateTimeOffset createdAtUtc,
        string actorSubjectId,
        string beneficiarySubjectId)
    {
        ArgumentNullException.ThrowIfNull(plan);
        ArgumentNullException.ThrowIfNull(monthlyAddonRates);
        if (tenantId == Guid.Empty || string.IsNullOrWhiteSpace(tenantIdentifier))
        {
            throw new ArgumentException("A tenant identity is required.");
        }

        if (string.IsNullOrWhiteSpace(actorSubjectId) || string.IsNullOrWhiteSpace(beneficiarySubjectId))
        {
            throw new ArgumentException("Quote actor and beneficiary are required.");
        }

        if (plan == TenantPlan.None || plan == TenantPlan.InactiveArchived || plan.IsTrial)
        {
            throw new ArgumentException("The selected plan cannot be purchased.", nameof(plan));
        }

        if (string.IsNullOrWhiteSpace(planVersion) || string.IsNullOrWhiteSpace(databaseProvider))
        {
            throw new ArgumentException("Plan version and database provider are required.");
        }

        if (databaseStrategy != plan.DatabaseStrategy)
        {
            throw new ArgumentException("Database strategy must match the selected plan.", nameof(databaseStrategy));
        }

        if (replicaCount < 0 || hasReadReplicas != (replicaCount > 0))
        {
            throw new ArgumentOutOfRangeException(nameof(replicaCount));
        }

        decimal ratePerReplica = 0m;
        if (hasReadReplicas && !monthlyAddonRates.TryGetValue("read-replica", out ratePerReplica))
        {
            throw new ArgumentException("Read replicas require a configured price.", nameof(monthlyAddonRates));
        }

        if (ratePerReplica < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(monthlyAddonRates));
        }

        if (monthlyAddonRates.Keys.Any(key => key != "read-replica"))
        {
            throw new ArgumentException("Unknown add-on pricing keys are not supported.", nameof(monthlyAddonRates));
        }

        var total = decimal.Round(plan.BasePrice + (ratePerReplica * replicaCount), 2, MidpointRounding.ToEven);
        if (total <= 0m || total > (long.MaxValue / 100m))
        {
            throw new ArgumentOutOfRangeException(nameof(plan));
        }

        return new TenantPurchaseOrder
        {
            TenantId = tenantId,
            TenantIdentifier = tenantIdentifier.Trim(),
            PlanId = plan.Name,
            PlanVersion = planVersion,
            AmountMinor = decimal.ToInt64(total * 100m),
            Currency = "USD",
            DatabaseStrategy = databaseStrategy.Name,
            DatabaseProvider = databaseProvider,
            HasReadReplicas = hasReadReplicas,
            ReplicaCount = replicaCount,
            Status = "pending",
            CreatedAtUtc = createdAtUtc,
            CreatedBySubjectId = actorSubjectId,
            BeneficiarySubjectId = beneficiarySubjectId,
        };
    }

    /// <summary>Correlates a pending renewal to the Customer-verified prior paid period.</summary>
    public void BindPreviousPurchaseOrder(Guid previousPurchaseOrderId)
    {
        if (Status != "pending" || PreviousPurchaseOrderId != Guid.Empty ||
            previousPurchaseOrderId == Guid.Empty || previousPurchaseOrderId == Id)
        {
            throw new InvalidOperationException("A renewal must name one distinct preceding paid order.");
        }

        PreviousPurchaseOrderId = previousPurchaseOrderId;
    }

    /// <summary>Associates one provider intent with the immutable pending purchase snapshot.</summary>
    /// <param name="paymentIntentId">The Stripe-issued PaymentIntent ID.</param>
    public void BindStripePaymentIntent(string paymentIntentId)
    {
        if (Status != "pending" || string.IsNullOrWhiteSpace(paymentIntentId) ||
            !paymentIntentId.StartsWith("pi_", StringComparison.Ordinal) ||
            StripePaymentIntentId is not null && StripePaymentIntentId != paymentIntentId)
        {
            throw new InvalidOperationException("Stripe intent cannot be attached to this purchase.");
        }

        StripePaymentIntentId = paymentIntentId;
    }

    /// <summary>Confirms an order after a trusted captured-payment outcome.</summary>
    /// <param name="subscriptionId">The stable subscription identifier.</param>
    /// <param name="confirmedAtUtc">The payment confirmation timestamp.</param>
    public void ConfirmCapturedPayment(Guid subscriptionId, DateTimeOffset confirmedAtUtc)
    {
        if (Status != "pending")
        {
            throw new InvalidOperationException("Only a pending purchase can be confirmed.");
        }

        if (subscriptionId == Guid.Empty)
        {
            throw new ArgumentException("A subscription identifier is required.", nameof(subscriptionId));
        }

        Confirm(subscriptionId, "captured", confirmedAtUtc);
    }

    /// <summary>Confirms a separately captured renewal payment without shortening a prepaid period.</summary>
    /// <param name="subscriptionId">The existing subscription identity.</param>
    /// <param name="previousValidUntilUtc">The end of the preceding verified paid period.</param>
    /// <param name="confirmedAtUtc">The time independently verified payment was captured.</param>
    public void ConfirmRenewalPayment(Guid subscriptionId, DateTimeOffset previousValidUntilUtc, DateTimeOffset confirmedAtUtc)
    {
        if (Status != "pending" || subscriptionId == Guid.Empty || previousValidUntilUtc == default || confirmedAtUtc == default)
        {
            throw new InvalidOperationException("A pending renewal and verified preceding paid period are required.");
        }

        Confirm(subscriptionId, "captured", confirmedAtUtc,
            previousValidUntilUtc > confirmedAtUtc ? previousValidUntilUtc : confirmedAtUtc);
    }

    /// <summary>Confirms an order after a privileged external invoice verification.</summary>
    /// <param name="subscriptionId">The stable subscription identifier.</param>
    /// <param name="invoiceReference">The verified invoice reference.</param>
    /// <param name="verifiedBy">The authenticated verifier.</param>
    /// <param name="confirmedAtUtc">The payment confirmation timestamp.</param>
    /// <param name="previousValidUntilUtc">Verified preceding paid period end when this invoice renews.</param>
    public void ConfirmExternalInvoice(Guid subscriptionId, string invoiceReference, string verifiedBy, DateTimeOffset confirmedAtUtc, DateTimeOffset? previousValidUntilUtc = null)
    {
        if (Status != "pending")
        {
            throw new InvalidOperationException("Only a pending purchase can be confirmed.");
        }

        if (subscriptionId == Guid.Empty)
        {
            throw new ArgumentException("A subscription identifier is required.", nameof(subscriptionId));
        }

        if (string.IsNullOrWhiteSpace(invoiceReference))
        {
            throw new ArgumentException("An invoice reference is required.", nameof(invoiceReference));
        }

        if (string.IsNullOrWhiteSpace(verifiedBy))
        {
            throw new ArgumentException("The verifying principal is required.", nameof(verifiedBy));
        }

        PaymentMethod = "external_invoice";
        ExternalInvoiceReference = invoiceReference.Trim();
        InvoiceVerifiedBy = verifiedBy.Trim();
        Confirm(subscriptionId, "external_invoice", confirmedAtUtc,
            previousValidUntilUtc is { } expiry && expiry > confirmedAtUtc ? expiry : confirmedAtUtc);
    }

    /// <summary>Cancels an already-paid active subscription; pending PaymentIntents require provider cancellation first.</summary>
    /// <param name="cancelledAtUtc">The immutable effective cancellation time.</param>
    public void Cancel(DateTimeOffset cancelledAtUtc)
    {
        if (Status != "active" || SubscriptionId == Guid.Empty || ConfirmedAtUtc is null ||
            cancelledAtUtc == default || cancelledAtUtc < ConfirmedAtUtc)
        {
            throw new InvalidOperationException("Only a confirmed active subscription can be cancelled.");
        }

        CancelledAtUtc = cancelledAtUtc;
        Status = "cancelled";
    }

    /// <summary>Suspends an expired paid subscription when no verified renewal was recorded.</summary>
    /// <param name="suspendedAtUtc">The effective expiry or later suspension time.</param>
    public void SuspendForFailedRenewal(DateTimeOffset suspendedAtUtc)
    {
        if (Status != "active" || SubscriptionId == Guid.Empty || ValidUntilUtc is not { } expiry ||
            suspendedAtUtc == default || suspendedAtUtc < expiry)
        {
            throw new InvalidOperationException("Only an expired paid subscription can be suspended for failed renewal.");
        }

        CancelledAtUtc = expiry;
        Status = "suspended";
    }

    private void Confirm(Guid subscriptionId, string paymentMethod, DateTimeOffset confirmedAtUtc, DateTimeOffset? validFromUtc = null)
    {
        SubscriptionId = subscriptionId;
        PaymentMethod = paymentMethod;
        Status = "active";
        ConfirmedAtUtc = confirmedAtUtc;
        ValidFromUtc = validFromUtc ?? confirmedAtUtc;
        ValidUntilUtc = ValidFromUtc.Value.AddYears(1);
        AddDomainEvent(new SubscriptionPaymentConfirmed(
            SubscriptionId, TenantId, Id, PlanId, PlanVersion, AmountMinor, Currency,
            DatabaseStrategy, DatabaseProvider, HasReadReplicas, ReplicaCount, PaymentMethod,
            confirmedAtUtc, ValidFromUtc.Value, ValidUntilUtc.Value));
    }
}
