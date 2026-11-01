namespace Billings.Host.Endpoints.PurgeFences;

/// <summary>Requests an exclusive Billing authorization to purge a tenant.</summary>
/// <param name="SubscriptionId">The subscription being purged.</param>
/// <param name="SourceOrderId">The exact approved Billing paid period.</param>
/// <param name="ApprovalId">The Customer approval event identifier.</param>
public sealed record AcquireBillingPurgeFenceRequest(Guid SubscriptionId, Guid SourceOrderId, Guid ApprovalId);
