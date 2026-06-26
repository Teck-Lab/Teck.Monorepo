// <copyright file="VaultTenantConnectionProvider.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using VaultSharp;
using VaultSharp.V1.AuthMethods;
using VaultSharp.V1.AuthMethods.Kubernetes;
using VaultSharp.V1.AuthMethods.Token;

namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// Reads tenant database connection strings from OpenBao/Vault KV v2.
/// Secrets are stored at <c>{kvMount}/tenants/{tenantIdentifier}/{serviceName}</c>.
/// Results are cached in memory with a configurable TTL.
/// </summary>
public sealed class VaultTenantConnectionProvider : IVaultTenantConnectionProvider
{
    private const string KubernetesTokenPath = "/var/run/secrets/kubernetes.io/serviceaccount/token";

    private readonly OpenBaoOptions _options;
    private readonly string _serviceName;
    private readonly ILogger<VaultTenantConnectionProvider> _logger;
    private readonly TimeSpan _cacheTtl;

    private readonly ConcurrentDictionary<string, (string Write, string? Read)> _cache
        = new(StringComparer.OrdinalIgnoreCase);

    private readonly ConcurrentDictionary<string, DateTimeOffset> _cacheExpiry
        = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>
    /// Initializes a new instance of the <see cref="VaultTenantConnectionProvider"/> class.
    /// </summary>
    /// <param name="options">OpenBao configuration options.</param>
    /// <param name="serviceName">
    /// The service name used as the last path segment in Vault
    /// (e.g. <c>catalog</c>, <c>orders</c>).
    /// </param>
    /// <param name="logger">Logger.</param>
    public VaultTenantConnectionProvider(
        OpenBaoOptions options,
        string serviceName,
        ILogger<VaultTenantConnectionProvider> logger)
    {
        _options = options;
        _serviceName = serviceName;
        _logger = logger;
        _cacheTtl = TimeSpan.FromSeconds(options.CacheTtlSeconds > 0 ? options.CacheTtlSeconds : 300);
    }

    /// <inheritdoc/>
    public bool TryGetCached(string tenantIdentifier, out (string Write, string? Read) result)
    {
        if (_cache.TryGetValue(tenantIdentifier, out result))
        {
            if (_cacheExpiry.TryGetValue(tenantIdentifier, out var expiry) && DateTimeOffset.UtcNow < expiry)
            {
                return true;
            }

            // Expired — evict
            _cache.TryRemove(tenantIdentifier, out _);
            _cacheExpiry.TryRemove(tenantIdentifier, out _);
        }

        result = default;
        return false;
    }

    /// <inheritdoc/>
    public async Task<(string Write, string? Read)> GetAsync(
        string tenantIdentifier,
        CancellationToken ct = default)
    {
        if (TryGetCached(tenantIdentifier, out var cached))
        {
            return cached;
        }

        string path = $"tenants/{tenantIdentifier}/{_serviceName}";
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Fetching tenant connection string from OpenBao for tenant '{TenantIdentifier}'/{ServiceName}",
                tenantIdentifier,
                _serviceName);
        }

        IVaultClient client = CreateClient();

        var secret = await client.V1.Secrets.KeyValue.V2
            .ReadSecretAsync(path: path, mountPoint: _options.KvMount)
            .ConfigureAwait(false);

        if (secret?.Data?.Data == null)
        {
            throw new TenantConnectionNotFoundException(
                $"No secret found in OpenBao for tenant '{tenantIdentifier}' service '{_serviceName}' " +
                $"at {_options.KvMount}/{path}.");
        }

        string write = RequireField(secret.Data.Data, "write", tenantIdentifier, path);

        secret.Data.Data.TryGetValue("read", out var readObj);
        string? read = readObj?.ToString();
        if (string.IsNullOrWhiteSpace(read))
        {
            read = null;
        }

        var result = (write, read);
        _cache[tenantIdentifier] = result;
        _cacheExpiry[tenantIdentifier] = DateTimeOffset.UtcNow.Add(_cacheTtl);

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            _logger.LogDebug(
                "Cached tenant connection string for tenant '{TenantIdentifier}' service '{ServiceName}' (TTL {TtlSeconds}s)",
                tenantIdentifier,
                _serviceName,
                _cacheTtl.TotalSeconds);
        }

        return result;
    }

    private static string RequireField(
        IDictionary<string, object> data,
        string key,
        string tenantIdentifier,
        string path)
    {
        if (!data.TryGetValue(key, out var value)
            || value is null
            || string.IsNullOrWhiteSpace(value.ToString()))
        {
            throw new TenantConnectionNotFoundException(
                $"Required field '{key}' missing or empty in OpenBao secret " +
                $"for tenant '{tenantIdentifier}' at path '{path}'.");
        }

        return value.ToString()!;
    }

    private IVaultClient CreateClient()
    {
        IAuthMethodInfo authMethod;

        if (!string.IsNullOrWhiteSpace(_options.Token))
        {
            authMethod = new TokenAuthMethodInfo(_options.Token);
        }
        else
        {
            if (string.IsNullOrWhiteSpace(_options.Role))
            {
                throw new InvalidOperationException(
                    "OpenBao Kubernetes auth requires 'OpenBao:Role' to be configured.");
            }

            string jwt = File.ReadAllText(KubernetesTokenPath);
            authMethod = new KubernetesAuthMethodInfo(
                string.IsNullOrWhiteSpace(_options.AuthPath) ? "kubernetes" : _options.AuthPath,
                _options.Role,
                jwt);
        }

        return new VaultClient(new VaultClientSettings(_options.Url, authMethod));
    }
}
