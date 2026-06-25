// <copyright file="WolverineTenantConnectionSource.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using JasperFx.Descriptors;
using JasperFx.MultiTenancy;

namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// Dynamic tenant source used by Wolverine for per-tenant message storage connections.
/// </summary>
public sealed class WolverineTenantConnectionSource : IDynamicTenantSource<string>
{
    private readonly ConcurrentDictionary<string, string> activeTenants = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, string> disabledTenants = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> tenantResolutionLocks = new(StringComparer.OrdinalIgnoreCase);
    private Func<string, CancellationToken, Task<string?>>? missingTenantResolver;
    private bool strictTenantResolution;

    /// <summary>
    /// Gets the default shared-database write connection string.
    /// </summary>
    public string DefaultWriteConnectionString { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="WolverineTenantConnectionSource"/> class.
    /// </summary>
    /// <param name="defaultWriteConnectionString">The default shared database connection string.</param>
    public WolverineTenantConnectionSource(string defaultWriteConnectionString)
    {
        this.DefaultWriteConnectionString = defaultWriteConnectionString;
    }

    /// <inheritdoc/>
    public Task AddTenantAsync(string tenantId, string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);
        ArgumentException.ThrowIfNullOrWhiteSpace(value);

        this.activeTenants[tenantId] = value;
        this.disabledTenants.TryRemove(tenantId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task DisableTenantAsync(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (this.activeTenants.TryRemove(tenantId, out string? value))
        {
            this.disabledTenants[tenantId] = value;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task RemoveTenantAsync(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        this.activeTenants.TryRemove(tenantId, out _);
        this.disabledTenants.TryRemove(tenantId, out _);
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public Task<IReadOnlyList<string>> AllDisabledAsync()
    {
        IReadOnlyList<string> disabled = this.disabledTenants.Keys.ToList();
        return Task.FromResult(disabled);
    }

    /// <inheritdoc/>
    public Task EnableTenantAsync(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (this.disabledTenants.TryRemove(tenantId, out string? value))
        {
            this.activeTenants[tenantId] = value;
        }

        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public DatabaseCardinality Cardinality => DatabaseCardinality.DynamicMultiple;

    /// <summary>
    /// Registers an on-demand resolver used when a tenant mapping is missing.
    /// </summary>
    /// <param name="resolver">Resolver that returns a connection string for the tenant, or null when unavailable.</param>
    public void SetMissingTenantResolver(Func<string, CancellationToken, Task<string?>> resolver)
    {
        ArgumentNullException.ThrowIfNull(resolver);
        this.missingTenantResolver = resolver;
    }

    /// <summary>
    /// Enables or disables strict tenant resolution behavior.
    /// </summary>
    /// <param name="enabled">True to throw on unresolved tenant mappings; false to fallback to shared connection.</param>
    public void SetStrictTenantResolution(bool enabled)
    {
        this.strictTenantResolution = enabled;
    }

    /// <inheritdoc/>
    public ValueTask<string> FindAsync(string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        if (this.activeTenants.TryGetValue(tenantId, out string? value))
        {
            return ValueTask.FromResult(value);
        }

        if (this.disabledTenants.ContainsKey(tenantId) || this.missingTenantResolver is null)
        {
            if (this.strictTenantResolution)
            {
                throw new TenantConnectionNotFoundException($"Tenant '{tenantId}' is not resolvable for Wolverine message persistence.");
            }

            return ValueTask.FromResult(this.DefaultWriteConnectionString);
        }

        return new ValueTask<string>(this.ResolveMissingTenantAsync(tenantId));
    }

    /// <inheritdoc/>
    public Task RefreshAsync()
    {
        return Task.CompletedTask;
    }

    /// <inheritdoc/>
    public IReadOnlyList<string> AllActive()
    {
        return this.activeTenants.Values
            .Distinct(StringComparer.Ordinal)
            .ToList();
    }

    /// <inheritdoc/>
    public IReadOnlyList<Assignment<string>> AllActiveByTenant()
    {
        return this.activeTenants
            .Select(pair => new Assignment<string>(pair.Key, pair.Value))
            .ToList();
    }

    private async Task<string> ResolveMissingTenantAsync(string tenantId)
    {
        if (this.activeTenants.TryGetValue(tenantId, out string? existing))
        {
            return existing;
        }

        if (this.disabledTenants.ContainsKey(tenantId))
        {
            if (this.strictTenantResolution)
            {
                throw new TenantConnectionNotFoundException($"Tenant '{tenantId}' is disabled for Wolverine message persistence.");
            }

            return this.DefaultWriteConnectionString;
        }

        Func<string, CancellationToken, Task<string?>>? resolver = this.missingTenantResolver;
        if (resolver is null)
        {
            if (this.strictTenantResolution)
            {
                throw new TenantConnectionNotFoundException($"No tenant resolver is configured for tenant '{tenantId}'.");
            }

            return this.DefaultWriteConnectionString;
        }

        SemaphoreSlim resolutionLock = this.tenantResolutionLocks.GetOrAdd(tenantId, static _ => new SemaphoreSlim(1, 1));
        await resolutionLock.WaitAsync(CancellationToken.None).ConfigureAwait(false);

        try
        {
            if (this.activeTenants.TryGetValue(tenantId, out string? cached))
            {
                return cached;
            }

            if (this.disabledTenants.ContainsKey(tenantId))
            {
                if (this.strictTenantResolution)
                {
                    throw new TenantConnectionNotFoundException($"Tenant '{tenantId}' is disabled for Wolverine message persistence.");
                }

                return this.DefaultWriteConnectionString;
            }

            string? resolvedConnection = await resolver(tenantId, CancellationToken.None).ConfigureAwait(false);
            if (string.IsNullOrWhiteSpace(resolvedConnection))
            {
                if (this.strictTenantResolution)
                {
                    throw new TenantConnectionNotFoundException($"Tenant '{tenantId}' could not be resolved for Wolverine message persistence.");
                }

                return this.DefaultWriteConnectionString;
            }

            this.activeTenants[tenantId] = resolvedConnection;
            this.disabledTenants.TryRemove(tenantId, out _);
            return resolvedConnection;
        }
        finally
        {
            resolutionLock.Release();
        }
    }
}
