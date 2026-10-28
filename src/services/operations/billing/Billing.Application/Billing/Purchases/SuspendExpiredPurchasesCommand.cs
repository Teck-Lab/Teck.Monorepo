namespace Billings.Application.Billing.Purchases;

/// <summary>Reconciles already-expired paid subscriptions when a verified renewal has not extended validity.</summary>
/// <param name="NowUtc">The reconciliation cutoff in UTC.</param>
public sealed record SuspendExpiredPurchasesCommand(DateTimeOffset NowUtc);
