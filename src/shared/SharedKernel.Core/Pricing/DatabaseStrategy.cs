using Ardalis.SmartEnum;

namespace SharedKernel.Core.Pricing;

/// <summary>
/// Represents the database strategy used for pricing and configuration.
/// </summary>
public sealed class DatabaseStrategy : SmartEnum<DatabaseStrategy>
{
    /// <summary>
    /// Represents the absence of a database strategy.
    /// </summary>
    public static readonly DatabaseStrategy None = new(nameof(None), 0);

    /// <summary>
    /// Represents a shared database strategy.
    /// </summary>
    public static readonly DatabaseStrategy Shared = new(nameof(Shared), 1);

    /// <summary>
    /// Represents a dedicated database strategy.
    /// </summary>
    public static readonly DatabaseStrategy Dedicated = new(nameof(Dedicated), 2);

    /// <summary>
    /// Represents an external database strategy.
    /// </summary>
    public static readonly DatabaseStrategy External = new(nameof(External), 3);

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseStrategy"/> class.
    /// </summary>
    /// <param name="name">The name of the strategy.</param>
    /// <param name="value">The value of the strategy.</param>
    private DatabaseStrategy(string name, int value) : base(name, value) { }

    /// <summary>
    /// Gets a value indicating whether this strategy requires a dedicated database connection.
    /// </summary>
    public bool RequiresDedicatedConnection => this == Dedicated || this == External;

    /// <summary>
    /// Gets a value indicating whether this strategy supports read replicas.
    /// </summary>
    public bool SupportsReadReplicas => this != None;

    /// <summary>
    /// Gets a value indicating whether this strategy supports auto scaling.
    /// </summary>
    public bool SupportsAutoScaling => this == Dedicated || this == External;

    /// <summary>
    /// Gets a value indicating whether this strategy supports geographic distribution.
    /// </summary>
    public bool SupportsGeographicDistribution => this == Dedicated || this == External;

    /// <summary>
    /// Gets a value indicating whether this strategy supports compliance features.
    /// </summary>
    public bool SupportsComplianceFeatures => this != None;

    /// <summary>
    /// Gets a value indicating whether this strategy supports dedicated resources.
    /// </summary>
    public bool SupportsDedicatedResources => this == Dedicated || this == External;

    /// <summary>
    /// Gets a value indicating whether this strategy allows multi-tenant queries.
    /// </summary>
    public bool AllowsMultiTenantQueries => this == Shared;

    /// <summary>
    /// Gets the estimated relative cost multiplier for this strategy.
    /// </summary>
    public decimal CostMultiplier => this switch
    {
        _ when this == Shared => 1.0m,
        _ when this == Dedicated => 3.0m,
        _ when this == External => 1.5m,
        _ => 1.0m,
    };

    /// <summary>
    /// Gets the recommended backup frequency for this strategy.
    /// </summary>
    public TimeSpan RecommendedBackupInterval => this switch
    {
        _ when this == Shared => TimeSpan.FromHours(6),
        _ when this == Dedicated => TimeSpan.FromHours(4),
        _ when this == External => TimeSpan.FromHours(12),
        _ => TimeSpan.FromDays(1),
    };

    /// <summary>
    /// Gets the maximum number of concurrent connections recommended for this strategy.
    /// </summary>
    public int MaxRecommendedConnections => this switch
    {
        _ when this == Shared => 100,
        _ when this == Dedicated => 500,
        _ when this == External => 1000,
        _ => 10,
    };

    /// <summary>
    /// Gets the isolation level for this strategy.
    /// </summary>
    public string IsolationLevel => this switch
    {
        _ when this == Shared => "Tenant",
        _ when this == Dedicated => "Database",
        _ when this == External => "Instance",
        _ => "None",
    };

    /// <summary>
    /// Gets the recommended maintenance window for this strategy.
    /// </summary>
    public TimeSpan MaintenanceWindow => this switch
    {
        _ when this == Shared => TimeSpan.FromHours(2),
        _ when this == Dedicated => TimeSpan.FromHours(4),
        _ when this == External => TimeSpan.FromHours(8),
        _ => TimeSpan.FromHours(1),
    };

    /// <summary>
    /// Gets the service level agreement (SLA) uptime percentage for this strategy.
    /// </summary>
    public decimal SlaUptimePercentage => this switch
    {
        _ when this == Shared => 99.5m,
        _ when this == Dedicated => 99.9m,
        _ when this == External => 99.95m,
        _ => 95.0m,
    };

    /// <summary>
    /// Validates if the given database options are compatible with this strategy.
    /// </summary>
    /// <param name="options">The database options to validate.</param>
    /// <returns>True if compatible, otherwise false.</returns>
    public bool IsCompatibleWith(DatabaseOptions options)
    {
        if (options.HasReadReplicas && !SupportsReadReplicas)
        {
            return false;
        }

        if (options.RequiresAutoScaling && !SupportsAutoScaling)
        {
            return false;
        }

        if (options.RequiresGeographicDistribution && !SupportsGeographicDistribution)
        {
            return false;
        }

        if (options.RequiresDedicatedResources && !SupportsDedicatedResources)
        {
            return false;
        }

        return true;
    }
}
