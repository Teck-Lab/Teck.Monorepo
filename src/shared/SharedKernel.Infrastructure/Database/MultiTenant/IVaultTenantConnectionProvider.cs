// <copyright file="IVaultTenantConnectionProvider.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// Resolves tenant-specific database connection strings from OpenBao/Vault KV.
/// Secrets are stored at <c>{kvMount}/tenants/{tenantIdentifier}/{serviceName}</c>
/// with fields <c>write</c> (required) and <c>read</c> (optional).
/// </summary>
public interface IVaultTenantConnectionProvider
{
    /// <summary>
    /// Attempts to return a previously cached connection string without making a Vault call.
    /// </summary>
    /// <param name="tenantIdentifier">The tenant slug or GUID used as the Vault path segment.</param>
    /// <param name="result">The cached write/read connection strings if found and not expired.</param>
    /// <returns><see langword="true"/> if a valid cached entry was found; otherwise <see langword="false"/>.</returns>
    bool TryGetCached(string tenantIdentifier, out (string Write, string? Read) result);

    /// <summary>
    /// Fetches the tenant connection string from Vault (or in-memory cache if available).
    /// </summary>
    /// <param name="tenantIdentifier">The tenant slug or GUID used as the Vault path segment.</param>
    /// <param name="ct">Cancellation token.</param>
    /// <returns>A tuple of write and optional read connection strings.</returns>
    /// <exception cref="TenantConnectionNotFoundException">
    /// Thrown when the secret does not exist in Vault or required fields are missing.
    /// </exception>
    Task<(string Write, string? Read)> GetAsync(string tenantIdentifier, CancellationToken ct = default);
}
