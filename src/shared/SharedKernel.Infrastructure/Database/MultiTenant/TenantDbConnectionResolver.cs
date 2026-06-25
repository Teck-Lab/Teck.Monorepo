// <copyright file="TenantDbConnectionResolver.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Pricing;
using SharedKernel.Infrastructure.MultiTenant;

namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// Resolves the database connection string for the current tenant.
///
/// Resolution logic â€” driven by the <c>X-Tenant-DbStrategy</c> gateway header:
/// <list type="bullet">
///   <item><c>Shared</c> â†’ returns the service's shared default connection strings (no Vault call).</item>
///   <item><c>Dedicated</c> / <c>External</c> â†’ fetches the tenant's connection string from OpenBao/Vault KV.</item>
///   <item>Header absent â†’ uses <see cref="OpenBaoOptions.DefaultStrategy"/> (defaults to Shared).</item>
/// </list>
/// </summary>
public class TenantDbConnectionResolver : ITenantDbConnectionResolver
{
    private const string TenantDbStrategyHeader = "X-Tenant-DbStrategy";

    private readonly string _defaultWriteConnectionString;
    private readonly string _defaultReadConnectionString;
    private readonly DatabaseProvider _defaultProvider;
    private readonly DatabaseStrategy _defaultStrategy;
    private readonly ILogger<TenantDbConnectionResolver> _logger;
    private readonly Lazy<IHttpContextAccessor?> _httpContextAccessor;
    private readonly IVaultTenantConnectionProvider _vaultProvider;

    /// <summary>
    /// Initializes a new instance of the <see cref="TenantDbConnectionResolver"/> class.
    /// </summary>
    /// <param name="serviceProvider">Service provider (for ILogger, IHttpContextAccessor).</param>
    /// <param name="defaultWriteConnectionString">Shared-strategy write connection string.</param>
    /// <param name="defaultReadConnectionString">Shared-strategy read connection string.</param>
    /// <param name="defaultProvider">Default database provider.</param>
    /// <param name="vaultProvider">
    /// Vault connection provider for dedicated-strategy tenants.
    /// When <see langword="null"/>, a <see cref="NullVaultTenantConnectionProvider"/> is used
    /// (dedicated resolution will throw if attempted).
    /// </param>
    /// <param name="defaultStrategy">
    /// Fallback strategy name when the gateway header is absent. Defaults to <c>Shared</c>.
    /// </param>
    public TenantDbConnectionResolver(
        IServiceProvider serviceProvider,
        string defaultWriteConnectionString,
        string defaultReadConnectionString,
        DatabaseProvider defaultProvider,
        IVaultTenantConnectionProvider? vaultProvider = null,
        string defaultStrategy = "Shared")
    {
        ArgumentNullException.ThrowIfNull(serviceProvider);

        _defaultWriteConnectionString = defaultWriteConnectionString;
        _defaultReadConnectionString = defaultReadConnectionString;
        _defaultProvider = defaultProvider;
        _vaultProvider = vaultProvider ?? new NullVaultTenantConnectionProvider();

        _logger = serviceProvider.GetService<ILogger<TenantDbConnectionResolver>>()
            ?? throw new InvalidOperationException("Logger service is not registered.");

        _httpContextAccessor = new Lazy<IHttpContextAccessor?>(
            () => serviceProvider.GetService<IHttpContextAccessor>());

        DatabaseStrategy.TryFromName(defaultStrategy, ignoreCase: true, out var parsedDefault);
        _defaultStrategy = parsedDefault ?? DatabaseStrategy.Shared;
    }

    /// <inheritdoc/>
    public (string WriteConnectionString, string? ReadConnectionString, DatabaseProvider Provider, DatabaseStrategy Strategy)
        ResolveTenantConnection(TenantDetails tenantInfo)
    {
        if (tenantInfo == null)
        {
            return (_defaultWriteConnectionString, _defaultReadConnectionString, _defaultProvider, DatabaseStrategy.Shared);
        }

        var strategy = ReadStrategyHeader() ?? _defaultStrategy;

        if (strategy == DatabaseStrategy.Shared)
        {
            return GetSharedConnection(tenantInfo);
        }

        // Dedicated / External â€” fetch from Vault
        string identifier = ResolveIdentifier(tenantInfo);
        var provider = ResolveProvider(tenantInfo);

        if (_vaultProvider.TryGetCached(identifier, out var cached))
        {
            string? read = tenantInfo.HasReadReplicas ? cached.Read : null;
            return (cached.Write, read, provider, strategy);
        }

        // Cache miss â€” block on async (first request per tenant; Vault is in-cluster)
        var vaultResult = _vaultProvider.GetAsync(identifier, CancellationToken.None).GetAwaiter().GetResult();
        return (vaultResult.Write, tenantInfo.HasReadReplicas ? vaultResult.Read : null, provider, strategy);
    }

    /// <inheritdoc/>
    public async Task<TenantConnectionResult> ResolveTenantConnectionSafelyAsync(
        TenantDetails tenantInfo,
        bool requireCustomerApi = true)
    {
        if (tenantInfo == null)
        {
            return TenantConnectionResult.Success(
                _defaultWriteConnectionString,
                _defaultReadConnectionString,
                _defaultProvider,
                DatabaseStrategy.Shared,
                customerApiAvailable: true,
                fromCache: false);
        }

        try
        {
            var strategy = ReadStrategyHeader() ?? _defaultStrategy;

            if (strategy == DatabaseStrategy.Shared)
            {
                var (write, read, provider, _) = GetSharedConnection(tenantInfo);
                return TenantConnectionResult.Success(
                    write,
                    read,
                    provider,
                    DatabaseStrategy.Shared,
                    customerApiAvailable: true,
                    fromCache: false);
            }

            string identifier = ResolveIdentifier(tenantInfo);
            bool fromCache = _vaultProvider.TryGetCached(identifier, out var cached);
            (string Write, string? Read) vaultResult = fromCache
                ? cached
                : await _vaultProvider.GetAsync(identifier).ConfigureAwait(false);

            var resolvedProvider = ResolveProvider(tenantInfo);
            string? readConnection = tenantInfo.HasReadReplicas ? vaultResult.Read : null;
            return TenantConnectionResult.Success(
                vaultResult.Write,
                readConnection,
                resolvedProvider,
                strategy,
                customerApiAvailable: true,
                fromCache: fromCache);
        }
        catch (TenantConnectionNotFoundException vaultException)
        {
            _logger.LogError(vaultException, "Tenant connection not found in OpenBao for tenant {TenantId}", tenantInfo.Id);
            return TenantConnectionResult.Failure(vaultException.Message);
        }
        catch (Exception resolveException)
        {
            _logger.LogError(resolveException, "Error resolving tenant database connection for tenant {TenantId}", tenantInfo.Id);
            return TenantConnectionResult.Failure(
                $"Failed to resolve tenant connection for '{tenantInfo.Id}': {resolveException.Message}");
        }
    }

    private DatabaseStrategy? ReadStrategyHeader()
    {
        var headers = _httpContextAccessor.Value?.HttpContext?.Request?.Headers;
        if (headers == null)
        {
            return null;
        }

        if (!headers.TryGetValue(TenantDbStrategyHeader, out var strategyHeader)
            || string.IsNullOrWhiteSpace(strategyHeader))
        {
            return null;
        }

        DatabaseStrategy.TryFromName(strategyHeader.ToString(), ignoreCase: true, out var strategy);
        return strategy;
    }

    private (string WriteConnectionString, string? ReadConnectionString, DatabaseProvider Provider, DatabaseStrategy Strategy)
        GetSharedConnection(TenantDetails tenantInfo)
    {
        var provider = ResolveProvider(tenantInfo);
        return (_defaultWriteConnectionString, _defaultReadConnectionString, provider, DatabaseStrategy.Shared);
    }

    private DatabaseProvider ResolveProvider(TenantDetails tenantInfo)
    {
        DatabaseProvider.TryFromName(tenantInfo.DatabaseProvider, ignoreCase: true, out var provider);
        return provider ?? _defaultProvider;
    }

    private static string ResolveIdentifier(TenantDetails tenantInfo)
    {
        if (!string.IsNullOrWhiteSpace(tenantInfo.Identifier))
        {
            return tenantInfo.Identifier;
        }

        if (!string.IsNullOrWhiteSpace(tenantInfo.Id))
        {
            return tenantInfo.Id;
        }

        throw new InvalidOperationException("Tenant has no Identifier or Id set.");
    }
}
