namespace SharedKernel.Core.Pricing;

/// <summary>
/// Represents additional database options that can be applied to any strategy.
/// </summary>
public class DatabaseOptions
{
    /// <summary>
    /// Gets or sets a value indicating whether the tenant has dedicated read replicas for improved read performance.
    /// Can be applied to any DatabaseStrategy for an additional cost.
    /// </summary>
    public bool HasReadReplicas { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tenant requires geographic distribution of databases.
    /// </summary>
    public bool RequiresGeographicDistribution { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tenant requires enhanced backup and disaster recovery.
    /// </summary>
    public bool RequiresEnhancedBackup { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tenant requires encryption at rest.
    /// </summary>
    public bool RequiresEncryptionAtRest { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tenant requires dedicated CPU/memory resources.
    /// </summary>
    public bool RequiresDedicatedResources { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tenant requires automatic scaling capabilities.
    /// </summary>
    public bool RequiresAutoScaling { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tenant requires advanced monitoring and alerting.
    /// </summary>
    public bool RequiresAdvancedMonitoring { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the tenant requires compliance features (GDPR, HIPAA, etc.).
    /// </summary>
    public bool RequiresComplianceFeatures { get; set; }

    /// <summary>
    /// Gets a value indicating whether any options are enabled.
    /// </summary>
    public bool HasAnyOptions => HasReadReplicas || RequiresGeographicDistribution || RequiresEnhancedBackup ||
                                RequiresEncryptionAtRest || RequiresDedicatedResources || RequiresAutoScaling ||
                                RequiresAdvancedMonitoring || RequiresComplianceFeatures;

    /// <summary>
    /// Gets the number of enabled options.
    /// </summary>
    public int EnabledOptionsCount =>
        (HasReadReplicas ? 1 : 0) +
        (RequiresGeographicDistribution ? 1 : 0) +
        (RequiresEnhancedBackup ? 1 : 0) +
        (RequiresEncryptionAtRest ? 1 : 0) +
        (RequiresDedicatedResources ? 1 : 0) +
        (RequiresAutoScaling ? 1 : 0) +
        (RequiresAdvancedMonitoring ? 1 : 0) +
        (RequiresComplianceFeatures ? 1 : 0);

    /// <summary>
    /// Gets default database options with no additional features.
    /// </summary>
    public static DatabaseOptions Default => new();

    /// <summary>
    /// Gets database options for high availability scenarios.
    /// </summary>
    public static DatabaseOptions HighAvailability => new()
    {
        HasReadReplicas = true,
        RequiresGeographicDistribution = true,
        RequiresEnhancedBackup = true,
        RequiresEncryptionAtRest = true,
        RequiresDedicatedResources = true,
        RequiresAutoScaling = true,
        RequiresAdvancedMonitoring = true
    };

    /// <summary>
    /// Gets database options for performance scenarios (read replicas and auto scaling).
    /// </summary>
    public static DatabaseOptions Performance => new()
    {
        HasReadReplicas = true,
        RequiresAutoScaling = true,
        RequiresDedicatedResources = true
    };

    /// <summary>
    /// Gets database options for backup scenarios (enhanced backup only).
    /// </summary>
    public static DatabaseOptions Backup => new()
    {
        RequiresEnhancedBackup = true
    };

    /// <summary>
    /// Gets database options for security scenarios (encryption and compliance).
    /// </summary>
    public static DatabaseOptions Security => new()
    {
        RequiresEnhancedBackup = true,
        RequiresEncryptionAtRest = true,
        RequiresComplianceFeatures = true,
        RequiresAdvancedMonitoring = true
    };

    /// <summary>
    /// Gets database options for compliance scenarios (all security and audit features).
    /// </summary>
    public static DatabaseOptions Compliance => new()
    {
        RequiresGeographicDistribution = true,
        RequiresEnhancedBackup = true,
        RequiresEncryptionAtRest = true,
        RequiresComplianceFeatures = true,
        RequiresAdvancedMonitoring = true
    };

    /// <summary>
    /// Creates a new instance with read replicas option modified.
    /// </summary>
    /// <param name="enabled">Whether to enable read replicas.</param>
    /// <returns>A new <see cref="DatabaseOptions"/> instance with the read replicas option modified.</returns>
    public DatabaseOptions WithReadReplicas(bool enabled = true) => new()
    {
        HasReadReplicas = enabled,
        RequiresGeographicDistribution = RequiresGeographicDistribution,
        RequiresEnhancedBackup = RequiresEnhancedBackup,
        RequiresEncryptionAtRest = RequiresEncryptionAtRest,
        RequiresDedicatedResources = RequiresDedicatedResources,
        RequiresAutoScaling = RequiresAutoScaling,
        RequiresAdvancedMonitoring = RequiresAdvancedMonitoring,
        RequiresComplianceFeatures = RequiresComplianceFeatures
    };

    /// <summary>
    /// Creates a new instance with geographic distribution option modified.
    /// </summary>
    /// <param name="enabled">Whether to enable geographic distribution.</param>
    /// <returns>A new <see cref="DatabaseOptions"/> instance with the geographic distribution option modified.</returns>
    public DatabaseOptions WithGeographicDistribution(bool enabled = true) => new()
    {
        HasReadReplicas = HasReadReplicas,
        RequiresGeographicDistribution = enabled,
        RequiresEnhancedBackup = RequiresEnhancedBackup,
        RequiresEncryptionAtRest = RequiresEncryptionAtRest,
        RequiresDedicatedResources = RequiresDedicatedResources,
        RequiresAutoScaling = RequiresAutoScaling,
        RequiresAdvancedMonitoring = RequiresAdvancedMonitoring,
        RequiresComplianceFeatures = RequiresComplianceFeatures
    };

    /// <summary>
    /// Creates a new instance with enhanced backup option modified.
    /// </summary>
    /// <param name="enabled">Whether to enable enhanced backup.</param>
    /// <returns>A new <see cref="DatabaseOptions"/> instance with the enhanced backup option modified.</returns>
    public DatabaseOptions WithEnhancedBackup(bool enabled = true) => new()
    {
        HasReadReplicas = HasReadReplicas,
        RequiresGeographicDistribution = RequiresGeographicDistribution,
        RequiresEnhancedBackup = enabled,
        RequiresEncryptionAtRest = RequiresEncryptionAtRest,
        RequiresDedicatedResources = RequiresDedicatedResources,
        RequiresAutoScaling = RequiresAutoScaling,
        RequiresAdvancedMonitoring = RequiresAdvancedMonitoring,
        RequiresComplianceFeatures = RequiresComplianceFeatures
    };

    /// <summary>
    /// Creates a new instance with encryption at rest option modified.
    /// </summary>
    /// <param name="enabled">Whether to enable encryption at rest.</param>
    /// <returns>A new <see cref="DatabaseOptions"/> instance with the encryption at rest option modified.</returns>
    public DatabaseOptions WithEncryptionAtRest(bool enabled = true) => new()
    {
        HasReadReplicas = HasReadReplicas,
        RequiresGeographicDistribution = RequiresGeographicDistribution,
        RequiresEnhancedBackup = RequiresEnhancedBackup,
        RequiresEncryptionAtRest = enabled,
        RequiresDedicatedResources = RequiresDedicatedResources,
        RequiresAutoScaling = RequiresAutoScaling,
        RequiresAdvancedMonitoring = RequiresAdvancedMonitoring,
        RequiresComplianceFeatures = RequiresComplianceFeatures
    };

    /// <summary>
    /// Creates a new instance with dedicated resources option modified.
    /// </summary>
    /// <param name="enabled">Whether to enable dedicated resources.</param>
    /// <returns>A new <see cref="DatabaseOptions"/> instance with the dedicated resources option modified.</returns>
    public DatabaseOptions WithDedicatedResources(bool enabled = true) => new()
    {
        HasReadReplicas = HasReadReplicas,
        RequiresGeographicDistribution = RequiresGeographicDistribution,
        RequiresEnhancedBackup = RequiresEnhancedBackup,
        RequiresEncryptionAtRest = RequiresEncryptionAtRest,
        RequiresDedicatedResources = enabled,
        RequiresAutoScaling = RequiresAutoScaling,
        RequiresAdvancedMonitoring = RequiresAdvancedMonitoring,
        RequiresComplianceFeatures = RequiresComplianceFeatures
    };

    /// <summary>
    /// Creates a new instance with auto scaling option modified.
    /// </summary>
    /// <param name="enabled">Whether to enable auto scaling.</param>
    /// <returns>A new <see cref="DatabaseOptions"/> instance with the auto scaling option modified.</returns>
    public DatabaseOptions WithAutoScaling(bool enabled = true) => new()
    {
        HasReadReplicas = HasReadReplicas,
        RequiresGeographicDistribution = RequiresGeographicDistribution,
        RequiresEnhancedBackup = RequiresEnhancedBackup,
        RequiresEncryptionAtRest = RequiresEncryptionAtRest,
        RequiresDedicatedResources = RequiresDedicatedResources,
        RequiresAutoScaling = enabled,
        RequiresAdvancedMonitoring = RequiresAdvancedMonitoring,
        RequiresComplianceFeatures = RequiresComplianceFeatures
    };

    /// <summary>
    /// Creates a new instance with advanced monitoring option modified.
    /// </summary>
    /// <param name="enabled">Whether to enable advanced monitoring.</param>
    /// <returns>A new <see cref="DatabaseOptions"/> instance with the advanced monitoring option modified.</returns>
    public DatabaseOptions WithAdvancedMonitoring(bool enabled = true) => new()
    {
        HasReadReplicas = HasReadReplicas,
        RequiresGeographicDistribution = RequiresGeographicDistribution,
        RequiresEnhancedBackup = RequiresEnhancedBackup,
        RequiresEncryptionAtRest = RequiresEncryptionAtRest,
        RequiresDedicatedResources = RequiresDedicatedResources,
        RequiresAutoScaling = RequiresAutoScaling,
        RequiresAdvancedMonitoring = enabled,
        RequiresComplianceFeatures = RequiresComplianceFeatures
    };

    /// <summary>
    /// Creates a new instance with compliance features option modified.
    /// </summary>
    /// <param name="enabled">Whether to enable compliance features.</param>
    /// <returns>A new <see cref="DatabaseOptions"/> instance with the compliance features option modified.</returns>
    public DatabaseOptions WithComplianceFeatures(bool enabled = true) => new()
    {
        HasReadReplicas = HasReadReplicas,
        RequiresGeographicDistribution = RequiresGeographicDistribution,
        RequiresEnhancedBackup = RequiresEnhancedBackup,
        RequiresEncryptionAtRest = RequiresEncryptionAtRest,
        RequiresDedicatedResources = RequiresDedicatedResources,
        RequiresAutoScaling = RequiresAutoScaling,
        RequiresAdvancedMonitoring = RequiresAdvancedMonitoring,
        RequiresComplianceFeatures = enabled
    };

    /// <summary>
    /// Creates a new instance with multiple options modified.
    /// </summary>
    /// <param name="hasReadReplicas">Whether to enable read replicas.</param>
    /// <param name="requiresGeographicDistribution">Whether to enable geographic distribution.</param>
    /// <param name="requiresEnhancedBackup">Whether to enable enhanced backup.</param>
    /// <param name="requiresEncryptionAtRest">Whether to enable encryption at rest.</param>
    /// <param name="requiresDedicatedResources">Whether to enable dedicated resources.</param>
    /// <param name="requiresAutoScaling">Whether to enable auto scaling.</param>
    /// <param name="requiresAdvancedMonitoring">Whether to enable advanced monitoring.</param>
    /// <param name="requiresComplianceFeatures">Whether to enable compliance features.</param>
    /// <returns>A new <see cref="DatabaseOptions"/> instance with the specified options.</returns>
    public DatabaseOptions With(
        bool? hasReadReplicas = null,
        bool? requiresGeographicDistribution = null,
        bool? requiresEnhancedBackup = null,
        bool? requiresEncryptionAtRest = null,
        bool? requiresDedicatedResources = null,
        bool? requiresAutoScaling = null,
        bool? requiresAdvancedMonitoring = null,
        bool? requiresComplianceFeatures = null) =>
        new()
        {
            HasReadReplicas = hasReadReplicas ?? HasReadReplicas,
            RequiresGeographicDistribution = requiresGeographicDistribution ?? RequiresGeographicDistribution,
            RequiresEnhancedBackup = requiresEnhancedBackup ?? RequiresEnhancedBackup,
            RequiresEncryptionAtRest = requiresEncryptionAtRest ?? RequiresEncryptionAtRest,
            RequiresDedicatedResources = requiresDedicatedResources ?? RequiresDedicatedResources,
            RequiresAutoScaling = requiresAutoScaling ?? RequiresAutoScaling,
            RequiresAdvancedMonitoring = requiresAdvancedMonitoring ?? RequiresAdvancedMonitoring,
            RequiresComplianceFeatures = requiresComplianceFeatures ?? RequiresComplianceFeatures
        };

    /// <summary>
    /// Gets the total cost multiplier including all options.
    /// </summary>
    /// <param name="baseStrategy">The base database strategy.</param>
    /// <returns>The total cost multiplier.</returns>
    public decimal GetTotalCostMultiplier(DatabaseStrategy baseStrategy)
    {
        var multiplier = baseStrategy.CostMultiplier;

        if (HasReadReplicas)
            multiplier += 1.5m;
        if (RequiresGeographicDistribution)
            multiplier += 0.8m;
        if (RequiresEnhancedBackup)
            multiplier += 0.3m;
        if (RequiresEncryptionAtRest)
            multiplier += 0.2m;
        if (RequiresDedicatedResources)
            multiplier += 1.0m;
        if (RequiresAutoScaling)
            multiplier += 0.4m;
        if (RequiresAdvancedMonitoring)
            multiplier += 0.2m;
        if (RequiresComplianceFeatures)
            multiplier += 0.5m;

        return multiplier;
    }

    /// <summary>
    /// Gets a value indicating whether the options are compatible with the strategy.
    /// </summary>
    /// <param name="strategy">The database strategy to validate against.</param>
    /// <returns>True if compatible, otherwise false.</returns>
    public bool IsCompatibleWith(DatabaseStrategy strategy) =>
        strategy.IsCompatibleWith(this);

    /// <summary>
    /// Gets a value indicating whether the options are compatible with the provider.
    /// </summary>
    /// <param name="provider">The database provider to validate against.</param>
    /// <returns>True if compatible, otherwise false.</returns>
    public bool IsCompatibleWith(DatabaseProvider provider) =>
        provider.IsCompatibleWith(this);

    /// <summary>
    /// Gets a value indicating whether the options are compatible with both strategy and provider.
    /// </summary>
    /// <param name="strategy">The database strategy to validate against.</param>
    /// <param name="provider">The database provider to validate against.</param>
    /// <returns>True if compatible with both, otherwise false.</returns>
    public bool IsCompatibleWith(DatabaseStrategy strategy, DatabaseProvider provider) =>
        IsCompatibleWith(strategy) && IsCompatibleWith(provider);

    /// <summary>
    /// Returns a string representation of the database options.
    /// </summary>
    public override string ToString()
    {
        if (!HasAnyOptions)
            return "Default";

        var features = new[]
        {
            HasReadReplicas ? "Read Replicas" : null,
            RequiresGeographicDistribution ? "Geographic Distribution" : null,
            RequiresEnhancedBackup ? "Enhanced Backup" : null,
            RequiresEncryptionAtRest ? "Encryption at Rest" : null,
            RequiresDedicatedResources ? "Dedicated Resources" : null,
            RequiresAutoScaling ? "Auto Scaling" : null,
            RequiresAdvancedMonitoring ? "Advanced Monitoring" : null,
            RequiresComplianceFeatures ? "Compliance Features" : null
        }.Where(feature => feature != null);

        return string.Join(", ", features);
    }
}
