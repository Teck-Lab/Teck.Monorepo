namespace SharedKernel.Core.Pricing;

/// <summary>
/// Represents usage metrics for pricing calculations, such as query count and storage requirements.
/// </summary>
public class UsageMetrics
{
    /// <summary>
    /// Gets or sets the estimated monthly query count.
    /// </summary>
    public long MonthlyQueryCount { get; set; }

    /// <summary>
    /// Gets or sets the storage requirements in GB.
    /// </summary>
    public int StorageRequirementsGb { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether high availability is required.
    /// </summary>
    public bool RequiresHighAvailability { get; set; }
}
