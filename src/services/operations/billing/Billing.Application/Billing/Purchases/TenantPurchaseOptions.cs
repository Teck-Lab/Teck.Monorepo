namespace Billings.Application.Billing.Purchases;

/// <summary>Server-owned quote and add-on pricing settings.</summary>
public sealed class TenantPurchaseOptions
{
    /// <summary>Configuration section name.</summary>
    public const string SectionName = "TenantPurchase";

    /// <summary>Gets or sets the immutable plan catalogue version.</summary>
    public string PlanVersion { get; set; } = "2026-10";

    /// <summary>Gets or sets the fixed allow-list of tenant database providers.</summary>
    public string[] AllowedDatabaseProviders { get; set; } = ["postgres"];

    /// <summary>Gets or sets monthly read-replica rate in USD.</summary>
    public decimal ReadReplicaMonthlyRate { get; set; }
}
