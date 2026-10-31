namespace Billings.Domain.Entities;

/// <summary>Durable lock record for one external tenant purge, retained until explicitly completed.</summary>
public sealed class TenantPurgeGrant
{
    private TenantPurgeGrant() { }

    /// <summary>Gets the stable worker-facing grant identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the globally unique tenant registry identifier.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the subscription under purge.</summary>
    public Guid SubscriptionId { get; private set; }

    /// <summary>Gets the exact paid source order the operators approved.</summary>
    public Guid SourceOrderId { get; private set; }

    /// <summary>Gets the approval correlation identifier.</summary>
    public Guid ApprovalId { get; private set; }

    /// <summary>Gets when the purge grant was durably acquired.</summary>
    public DateTimeOffset AcquiredAtUtc { get; private set; }

    /// <summary>Gets when external purge and reconciliation completed; null means hold/retry.</summary>
    public DateTimeOffset? CompletedAtUtc { get; private set; }

    /// <summary>Creates a durable active grant with validated identity and acquisition time.</summary>
    /// <param name="id">The stable grant identifier.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <param name="subscriptionId">The subscription under purge.</param>
    /// <param name="sourceOrderId">The approved paid source order.</param>
    /// <param name="approvalId">The approval correlation identifier.</param>
    /// <param name="acquiredAtUtc">The server-trusted acquisition time.</param>
    /// <returns>The active durable grant.</returns>
    /// <exception cref="ArgumentException">An identifier is empty or the acquisition time is missing.</exception>
    public static TenantPurgeGrant Create(Guid id, Guid tenantId, Guid subscriptionId, Guid sourceOrderId,
        Guid approvalId, DateTimeOffset acquiredAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || subscriptionId == Guid.Empty ||
            sourceOrderId == Guid.Empty || approvalId == Guid.Empty || acquiredAtUtc == default)
        {
            throw new ArgumentException("A purge grant requires complete identities and an acquisition time.");
        }

        return new TenantPurgeGrant
        {
            Id = id,
            TenantId = tenantId,
            SubscriptionId = subscriptionId,
            SourceOrderId = sourceOrderId,
            ApprovalId = approvalId,
            AcquiredAtUtc = acquiredAtUtc,
        };
    }

    /// <summary>Marks the externally completed purge as complete. Repeated completion is harmless.</summary>
    /// <param name="completedAtUtc">The server-trusted completion time.</param>
    /// <exception cref="ArgumentException">Completion precedes grant acquisition or has no timestamp.</exception>
    public void Complete(DateTimeOffset completedAtUtc)
    {
        if (completedAtUtc == default || completedAtUtc < AcquiredAtUtc)
        {
            throw new ArgumentException("Purge completion time must follow acquisition.", nameof(completedAtUtc));
        }

        CompletedAtUtc ??= completedAtUtc;
    }
}
