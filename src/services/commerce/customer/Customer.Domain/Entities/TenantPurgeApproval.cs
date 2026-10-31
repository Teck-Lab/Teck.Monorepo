namespace Customers.Domain.Entities;

/// <summary>An immutable Keycloak operator attestation to purge one paid, suspended tenant period.</summary>
public sealed class TenantPurgeApproval
{
    private TenantPurgeApproval() { }

    /// <summary>Gets this operator's immutable audit entry identifier.</summary>
    public Guid Id { get; private set; }

    /// <summary>Gets the global Customer registry tenant ID.</summary>
    public Guid TenantId { get; private set; }

    /// <summary>Gets the single paid source order being approved.</summary>
    public Guid SourceOrderId { get; private set; }

    /// <summary>Gets the subscription of the paid period.</summary>
    public Guid SubscriptionId { get; private set; }

    /// <summary>Gets the immutable Keycloak operator subject.</summary>
    public string ApproverSubjectId { get; private set; } = string.Empty;

    /// <summary>Gets the time the operator authorized the purge.</summary>
    public DateTimeOffset ApprovedAtUtc { get; private set; }

    /// <summary>Creates a single append-only audit entry; a different operator must make the second approval.</summary>
    /// <param name="id">Globally unique approval identifier.</param>
    /// <param name="tenantId">Canonical Customer tenant registry UUID.</param>
    /// <param name="sourceOrderId">Exact current paid order UUID.</param>
    /// <param name="subscriptionId">Stable subscription UUID.</param>
    /// <param name="approverSubjectId">Authenticated Keycloak operator subject.</param>
    /// <param name="approvedAtUtc">Server-trusted UTC approval time.</param>
    /// <returns>An immutable audit entry.</returns>
    public static TenantPurgeApproval Create(Guid id, Guid tenantId, Guid sourceOrderId, Guid subscriptionId,
        string approverSubjectId, DateTimeOffset approvedAtUtc)
    {
        if (id == Guid.Empty || tenantId == Guid.Empty || sourceOrderId == Guid.Empty || subscriptionId == Guid.Empty ||
            string.IsNullOrWhiteSpace(approverSubjectId) || approverSubjectId.Length > 128 ||
            approvedAtUtc == default || approvedAtUtc.Offset != TimeSpan.Zero)
        {
            throw new ArgumentException("A purge approval requires a paid tenant, authorized subject and UTC time.");
        }

        return new TenantPurgeApproval
        {
            Id = id, TenantId = tenantId, SourceOrderId = sourceOrderId, SubscriptionId = subscriptionId,
            ApproverSubjectId = approverSubjectId, ApprovedAtUtc = approvedAtUtc,
        };
    }
}
