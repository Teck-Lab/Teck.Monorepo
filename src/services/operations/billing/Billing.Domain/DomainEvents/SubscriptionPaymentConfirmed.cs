using SharedKernel.Core.Events;

namespace Billings.Domain.DomainEvents;

/// <summary>Raised when a purchase is confirmed by captured payment or audited invoice verification.</summary>
public sealed class SubscriptionPaymentConfirmed : DomainEvent
{
    /// <summary>Initializes a confirmed subscription domain event.</summary>
    /// <param name="subscriptionId">The stable subscription identifier.</param>
    /// <param name="tenantId">The global Customer tenant identifier.</param>
    /// <param name="sourceOrderId">The source purchase order identifier.</param>
    /// <param name="planId">The immutable plan identifier.</param>
    /// <param name="planVersion">The immutable plan version.</param>
    /// <param name="amountMinor">The confirmed amount in minor currency units.</param>
    /// <param name="currency">The ISO currency code.</param>
    /// <param name="databaseStrategy">The database strategy snapshot.</param>
    /// <param name="databaseProvider">The database provider snapshot.</param>
    /// <param name="hasReadReplicas">Whether read replicas were included.</param>
    /// <param name="replicaCount">The read replica count.</param>
    /// <param name="paymentMethod">The confirmed payment method.</param>
    /// <param name="confirmedAtUtc">The confirmation timestamp.</param>
    /// <param name="validFromUtc">The subscription start timestamp.</param>
    /// <param name="validUntilUtc">The subscription expiry timestamp.</param>
    public SubscriptionPaymentConfirmed(
        Guid subscriptionId, Guid tenantId, Guid sourceOrderId, string planId, string planVersion,
        long amountMinor, string currency, string databaseStrategy, string databaseProvider,
        bool hasReadReplicas, int replicaCount, string paymentMethod,
        DateTimeOffset confirmedAtUtc, DateTimeOffset validFromUtc, DateTimeOffset validUntilUtc)
    {
        SubscriptionId = subscriptionId;
        TenantId = tenantId;
        SourceOrderId = sourceOrderId;
        PlanId = planId;
        PlanVersion = planVersion;
        AmountMinor = amountMinor;
        Currency = currency;
        DatabaseStrategy = databaseStrategy;
        DatabaseProvider = databaseProvider;
        HasReadReplicas = hasReadReplicas;
        ReplicaCount = replicaCount;
        PaymentMethod = paymentMethod;
        ConfirmedAtUtc = confirmedAtUtc;
        ValidFromUtc = validFromUtc;
        ValidUntilUtc = validUntilUtc;
    }

    /// <summary>Gets the stable subscription identifier.</summary>
    public Guid SubscriptionId { get; }

    /// <summary>Gets the global Customer tenant identifier.</summary>
    public Guid TenantId { get; }

    /// <summary>Gets the source purchase order identifier.</summary>
    public Guid SourceOrderId { get; }

    /// <summary>Gets the immutable plan identifier.</summary>
    public string PlanId { get; }

    /// <summary>Gets the immutable plan version.</summary>
    public string PlanVersion { get; }

    /// <summary>Gets the confirmed amount in minor currency units.</summary>
    public long AmountMinor { get; }

    /// <summary>Gets the ISO currency code.</summary>
    public string Currency { get; }

    /// <summary>Gets the database strategy snapshot.</summary>
    public string DatabaseStrategy { get; }

    /// <summary>Gets the database provider snapshot.</summary>
    public string DatabaseProvider { get; }

    /// <summary>Gets whether the tenant plan includes read replicas.</summary>
    public bool HasReadReplicas { get; }

    /// <summary>Gets the replica count.</summary>
    public int ReplicaCount { get; }

    /// <summary>Gets the confirmed payment method.</summary>
    public string PaymentMethod { get; }

    /// <summary>Gets the confirmation timestamp.</summary>
    public DateTimeOffset ConfirmedAtUtc { get; }

    /// <summary>Gets the subscription start timestamp.</summary>
    public DateTimeOffset ValidFromUtc { get; }

    /// <summary>Gets the subscription expiry timestamp.</summary>
    public DateTimeOffset ValidUntilUtc { get; }
}
