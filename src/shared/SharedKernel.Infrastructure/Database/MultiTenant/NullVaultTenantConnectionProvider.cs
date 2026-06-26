// <copyright file="NullVaultTenantConnectionProvider.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// No-op implementation used when OpenBao is not configured (e.g. OpenBao:Url is empty).
/// Dedicated tenant resolution will fail explicitly if attempted.
/// </summary>
public sealed class NullVaultTenantConnectionProvider : IVaultTenantConnectionProvider
{
    /// <inheritdoc/>
    public bool TryGetCached(string tenantIdentifier, out (string Write, string? Read) result)
    {
        result = default;
        return false;
    }

    /// <inheritdoc/>
    public Task<(string Write, string? Read)> GetAsync(
        string tenantIdentifier,
        CancellationToken ct = default)
    {
        throw new TenantConnectionNotFoundException(
            $"OpenBao is not configured (OpenBao:Url is empty). " +
            $"Cannot resolve dedicated connection string for tenant '{tenantIdentifier}'.");
    }
}
