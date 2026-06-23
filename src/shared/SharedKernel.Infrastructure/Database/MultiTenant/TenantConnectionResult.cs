using SharedKernel.Core.Pricing;

namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// Represents the result of a tenant connection resolution operation.
/// </summary>
public class TenantConnectionResult
{
    /// <summary>
    /// Gets or sets the write connection string for the tenant.
    /// </summary>
    public string WriteConnectionString { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the read connection string for the tenant.
    /// </summary>
    public string? ReadConnectionString { get; set; }

    /// <summary>
    /// Gets or sets the database provider for the tenant.
    /// </summary>
    public DatabaseProvider Provider { get; set; } = DatabaseProvider.PostgreSQL;

    /// <summary>
    /// Gets or sets the database strategy for the tenant.
    /// </summary>
    public DatabaseStrategy Strategy { get; set; } = DatabaseStrategy.None;

    /// <summary>
    /// Gets or sets a value indicating whether the connection resolution was successful.
    /// </summary>
    public bool IsSuccess { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the connection resolution is safe for migration.
    /// For premium/enterprise tenants, this is false if the Customer API is unavailable.
    /// </summary>
    public bool IsSafeForMigration { get; set; }

    /// <summary>
    /// Gets or sets the error message if the connection resolution failed.
    /// </summary>
    public string? ErrorMessage { get; set; }

    /// <summary>
    /// Gets or sets the warning message if the connection resolution succeeded but with warnings.
    /// </summary>
    public string? WarningMessage { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the Customer API was available during resolution.
    /// </summary>
    public bool CustomerApiAvailable { get; set; }

    /// <summary>
    /// Gets or sets a value indicating whether the connection information was retrieved from cache.
    /// </summary>
    public bool FromCache { get; set; }

    /// <summary>
    /// Creates a successful connection result.
    /// </summary>
    /// <param name="writeConnectionString">The write connection string.</param>
    /// <param name="readConnectionString">The read connection string.</param>
    /// <param name="provider">The database provider.</param>
    /// <param name="strategy">The tenant database strategy.</param>
    /// <param name="customerApiAvailable">Whether the Customer API was available.</param>
    /// <param name="fromCache">Whether the result is from cache.</param>
    /// <returns>A successful tenant connection result.</returns>
    public static TenantConnectionResult Success(
        string writeConnectionString,
        string? readConnectionString,
        DatabaseProvider provider,
        DatabaseStrategy strategy,
        bool customerApiAvailable = true,
        bool fromCache = false)
    {
        return new TenantConnectionResult
        {
            WriteConnectionString = writeConnectionString,
            ReadConnectionString = readConnectionString,
            Provider = provider,
            Strategy = strategy,
            IsSuccess = true,
            IsSafeForMigration = true,
            CustomerApiAvailable = customerApiAvailable,
            FromCache = fromCache
        };
    }

    /// <summary>
    /// Creates a failed connection result.
    /// </summary>
    /// <param name="errorMessage">The error message.</param>
    /// <returns>A failed tenant connection result.</returns>
    public static TenantConnectionResult Failure(string errorMessage)
    {
        return new TenantConnectionResult
        {
            IsSuccess = false,
            IsSafeForMigration = false,
            ErrorMessage = errorMessage,
            CustomerApiAvailable = false
        };
    }

    /// <summary>
    /// Creates an unsafe connection result for premium/enterprise tenants when Customer API is unavailable.
    /// </summary>
    /// <param name="connectionString">The connection string.</param>
    /// <param name="provider">The database provider.</param>
    /// <param name="strategy">The tenant database strategy.</param>
    /// <param name="warningMessage">The warning message.</param>
    /// <returns>An unsafe tenant connection result.</returns>
    public static TenantConnectionResult UnsafeForMigration(
        string connectionString,
        DatabaseProvider provider,
        DatabaseStrategy strategy,
        string warningMessage)
    {
        return new TenantConnectionResult
        {
            WriteConnectionString = connectionString,
            Provider = provider,
            Strategy = strategy,
            IsSuccess = true,
            IsSafeForMigration = false,
            WarningMessage = warningMessage,
            CustomerApiAvailable = false,
            FromCache = true
        };
    }
}
