namespace Teck.TenantDatabase.Reconciler;

/// <summary>Independent worker retention policy; Customer's signed approval cannot shorten this window.</summary>
/// <param name="window">Configured positive period after Billing suspension.</param>
public sealed class TenantPurgeRetentionPolicy(TimeSpan window)
{
    /// <summary>Gets the positive, bounded recovery period or fails closed on missing configuration.</summary>
    public TimeSpan Window { get; } = window > TimeSpan.Zero && window <= TimeSpan.FromDays(3650)
        ? window : throw new InvalidOperationException("Tenant purge recovery days must be configured between 1 and 3650.");
}
