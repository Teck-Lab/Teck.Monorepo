using Ardalis.SmartEnum;

namespace SharedKernel.Core.Pricing;

/// <summary>
/// Represents the PostgreSQL database provider type using SmartEnum.
/// </summary>
public sealed class DatabaseProvider : SmartEnum<DatabaseProvider>
{
    /// <summary>
    /// PostgreSQL database provider (default, baseline cost).
    /// </summary>
    public static readonly DatabaseProvider PostgreSQL = new(nameof(PostgreSQL), 1, "PostgreSQL", "Npgsql", 1.0m);

    /// <summary>
    /// Gets the display name of the provider.
    /// </summary>
    public string DisplayName { get; }

    /// <summary>
    /// Gets the connection string provider name.
    /// </summary>
    public string ProviderName { get; }

    /// <summary>
    /// Gets the cost multiplier for this database provider.
    /// </summary>
    public decimal CostMultiplier { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseProvider"/> class.
    /// </summary>
    /// <param name="name">The name of the provider.</param>
    /// <param name="value">The value of the provider.</param>
    /// <param name="displayName">The display name of the provider.</param>
    /// <param name="providerName">The provider name for connection strings.</param>
    /// <param name="costMultiplier">The cost multiplier for this provider.</param>
    private DatabaseProvider(string name, int value, string displayName, string providerName, decimal costMultiplier)
        : base(name, value)
    {
        DisplayName = displayName;
        ProviderName = providerName;
        CostMultiplier = costMultiplier;
    }

    /// <summary>
    /// Checks if this provider is compatible with the specified database options.
    /// With PostgreSQL as the only provider, this is always true for PostgreSQL.
    /// </summary>
    /// <returns></returns>
    public bool IsCompatibleWith(DatabaseOptions options) => true;
}
