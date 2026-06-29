namespace SharedKernel.Grpc.Contracts.Remote.V1.Tenants;

/// <summary>Tenant database metadata returned by the customer service.</summary>
public sealed class TenantDatabaseInfoRpcResult
{
    /// <summary>Gets or sets a value indicating whether the tenant was found.</summary>
    public bool Found { get; set; }

    /// <summary>Gets or sets the tenant identifier.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant's unique identifier slug.</summary>
    public string Identifier { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant's database strategy (e.g. "shared", "dedicated").</summary>
    public string DatabaseStrategy { get; set; } = string.Empty;

    /// <summary>Gets or sets the tenant's database provider (e.g. "postgres").</summary>
    public string DatabaseProvider { get; set; } = string.Empty;

    /// <summary>Gets or sets a value indicating whether the tenant has read replicas.</summary>
    public bool HasReadReplicas { get; set; }

    /// <summary>Gets or sets a human-readable error detail when <see cref="Found"/> is false.</summary>
    public string? ErrorDetail { get; set; }
}
