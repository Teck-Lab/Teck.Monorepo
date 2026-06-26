using System.Collections.Concurrent;
using Finbuckle.MultiTenant.Abstractions;

namespace SharedKernel.Infrastructure.MultiTenant;

/// <summary>
/// Extension methods for <see cref="ITenantInfo"/>.
/// </summary>
public static class TenantInfoExtensions
{
    private static readonly ConcurrentDictionary<string, object> _tenantItems = new();

    /// <summary>
    /// Sets a custom item on the tenant info.
    /// </summary>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value.</param>
    public static void SetItem(this ITenantInfo tenantInfo, string key, object value)
    {
        var itemKey = $"{tenantInfo.Id}:{key}";
        _tenantItems.AddOrUpdate(itemKey, value, (_, _) => value);
    }

    /// <summary>
    /// Gets a custom item from the tenant info.
    /// </summary>
    /// <typeparam name="T">The type of the item.</typeparam>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <param name="key">The key.</param>
    /// <param name="defaultValue">The default value.</param>
    /// <returns>The item if found; otherwise, the default value.</returns>
    public static T GetItem<T>(this ITenantInfo tenantInfo, string key, T defaultValue = default!)
    {
        // Ensure tenantInfo and Id are not null
        if (tenantInfo?.Id == null)
        {
            return defaultValue;
        }

        var itemKey = $"{tenantInfo.Id}:{key}";
        if (_tenantItems.TryGetValue(itemKey, out var value) && value is T typedValue)
        {
            return typedValue;
        }

        return defaultValue;
    }

    /// <summary>
    /// Sets the connection string on the tenant info.
    /// </summary>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <param name="connectionString">The connection string.</param>
    public static void SetConnectionString(this ITenantInfo tenantInfo, string connectionString)
    {
        tenantInfo.SetItem("ConnectionString", connectionString);
    }

    /// <summary>
    /// Gets the connection string from the tenant info.
    /// </summary>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <returns>The connection string if found; otherwise, null.</returns>
    public static string GetConnectionString(this ITenantInfo tenantInfo)
    {
        return tenantInfo.GetItem<string>("ConnectionString");
    }

    /// <summary>
    /// Sets whether the tenant is primary.
    /// </summary>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <param name="isPrimary">Whether the tenant is primary.</param>
    public static void SetIsPrimary(this ITenantInfo tenantInfo, bool isPrimary)
    {
        tenantInfo.SetItem("IsPrimary", isPrimary);
    }

    /// <summary>
    /// Gets whether the tenant is primary.
    /// </summary>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <returns>True if the tenant is primary; otherwise, false.</returns>
    public static bool IsPrimary(this ITenantInfo tenantInfo)
    {
        return tenantInfo.GetItem<bool>("IsPrimary");
    }

    /// <summary>
    /// Tries to get a custom item from the tenant info.
    /// </summary>
    /// <param name="tenantInfo">The tenant info.</param>
    /// <param name="key">The key.</param>
    /// <param name="value">The value if found.</param>
    /// <returns>True if the item was found; otherwise, false.</returns>
    public static bool TryGetValue(this ITenantInfo tenantInfo, string key, out object? value)
    {
        // Ensure tenantInfo and Id are not null
        if (tenantInfo?.Id == null)
        {
            value = null;
            return false;
        }

        var itemKey = $"{tenantInfo.Id}:{key}";
        return _tenantItems.TryGetValue(itemKey, out value);
    }
}
