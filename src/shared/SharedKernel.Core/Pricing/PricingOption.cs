namespace SharedKernel.Core.Pricing;

/// <summary>
/// Represents a pricing option for a tenant, including plan, strategy, options, and calculated price.
/// </summary>
public class PricingOption
{
    /// <summary>
    /// Gets or sets the tenant plan for this pricing option.
    /// </summary>
    public TenantPlan Plan { get; set; } = TenantPlan.None;

    /// <summary>
    /// Gets or sets the database strategy for this pricing option.
    /// </summary>
    public DatabaseStrategy Strategy { get; set; } = default!;

    /// <summary>
    /// Gets or sets the database options for this pricing option.
    /// </summary>
    public DatabaseOptions Options { get; set; } = default!;

    /// <summary>
    /// Gets or sets the calculated monthly price for this option.
    /// </summary>
    public decimal MonthlyPrice { get; set; }

    /// <summary>
    /// Gets or sets the description of this pricing option.
    /// </summary>
    public string Description { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a value indicating whether this option is recommended.
    /// </summary>
    public bool IsRecommended { get; set; }
}
