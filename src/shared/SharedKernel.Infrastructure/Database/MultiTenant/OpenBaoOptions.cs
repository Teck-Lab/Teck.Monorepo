// <copyright file="OpenBaoOptions.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using System.Diagnostics.CodeAnalysis;

namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// Configuration options for OpenBao/Vault tenant connection string resolution.
/// Bind from the <c>OpenBao</c> configuration section.
/// </summary>
public sealed class OpenBaoOptions
{
    /// <summary>The configuration section key.</summary>
    public const string Section = "OpenBao";

    /// <summary>
    /// Gets or sets the OpenBao server URL.
    /// Leave empty to disable Vault-based resolution (uses <see cref="NullVaultTenantConnectionProvider"/>).
    /// </summary>
    [SuppressMessage("Design", "CA1056:URI-like properties should not be strings", Justification = "Configuration binding and VaultSharp require a plain string URL.")]
    public string Url { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets a static Vault token for authentication.
    /// When set, Kubernetes auth is bypassed — intended for local development only.
    /// </summary>
    public string? Token { get; set; }

    /// <summary>
    /// Gets or sets the Vault Kubernetes auth mount path.
    /// Defaults to <c>kubernetes</c>.
    /// </summary>
    public string AuthPath { get; set; } = "kubernetes";

    /// <summary>
    /// Gets or sets the Kubernetes auth role name bound to this service's ServiceAccount.
    /// Example: <c>catalog-api</c>.
    /// </summary>
    public string Role { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the KV v2 mount point that contains tenant secrets.
    /// Defaults to <c>teck-cloud</c>.
    /// </summary>
    public string KvMount { get; set; } = "teck-cloud";

    /// <summary>
    /// Gets or sets the fallback database strategy used when the
    /// <c>X-Tenant-DbStrategy</c> gateway header is absent.
    /// Defaults to <c>Shared</c>.
    /// </summary>
    public string DefaultStrategy { get; set; } = "Shared";

    /// <summary>
    /// Gets or sets how long (in seconds) resolved connection strings are cached in memory.
    /// Defaults to 300 (5 minutes).
    /// </summary>
    public int CacheTtlSeconds { get; set; } = 300;
}
