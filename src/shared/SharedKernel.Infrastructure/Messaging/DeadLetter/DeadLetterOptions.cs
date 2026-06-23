namespace SharedKernel.Infrastructure.Messaging.DeadLetter;

/// <summary>
/// Dead letter policy options.
/// </summary>
public sealed class DeadLetterOptions
{
    /// <summary>
    /// Configuration section name.
    /// </summary>
    public const string Section = "DeadLetter";

    /// <summary>
    /// Gets or sets a value indicating whether dead letter handling is enabled.
    /// </summary>
    public bool Enabled { get; set; } = true;

    /// <summary>
    /// Gets or sets the maximum retry count.
    /// </summary>
    public int MaxRetries { get; set; } = 3;

    /// <summary>
    /// Gets or sets the retention period in days.
    /// </summary>
    public int RetentionDays { get; set; } = 7;

    /// <summary>
    /// Gets or sets the dead letter queue name.
    /// </summary>
    public string DeadLetterQueue { get; set; } = "dead-letter-queue";
}
