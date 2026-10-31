namespace Customers.Application.Tenants;

/// <summary>Customer-owned recovery-window policy for independently approved database purges.</summary>
public sealed class TenantPurgeApprovalOptions
{
    /// <summary>Gets the configuration section name.</summary>
    public const string SectionName = "PurgeApproval";

    /// <summary>Gets or sets the minimum days after Billing suspension before either operator can approve purge.</summary>
    public int RecoveryDays { get; set; }

    /// <summary>Gets the validated positive recovery window; absent or unreasonable configuration denies purge.</summary>
    /// <returns>Configured recovery duration.</returns>
    public TimeSpan RequireRecoveryWindow() => RecoveryDays is > 0 and <= 3650
        ? TimeSpan.FromDays(RecoveryDays)
        : throw new InvalidOperationException("Customer purge recovery window must be configured between 1 and 3650 days.");
}
