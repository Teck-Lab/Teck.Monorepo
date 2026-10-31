namespace Billings.Host.Endpoints.PurgeFences;

/// <summary>Identifies a durable Billing purge grant.</summary>
/// <param name="GrantId">Stable grant ID required for completion.</param>
public sealed record AcquireBillingPurgeFenceResponse(Guid GrantId);
