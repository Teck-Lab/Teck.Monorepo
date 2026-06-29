using FastEndpoints;

namespace SharedKernel.Grpc.Contracts.Remote.V1.Tenants;

/// <summary>Requests tenant database metadata from the customer service.</summary>
public sealed class GetTenantDatabaseInfoCommand : ICommand<TenantDatabaseInfoRpcResult>
{
    /// <summary>Gets or sets the tenant identifier (GUID string).</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the downstream service name requesting the metadata.</summary>
    public string ServiceName { get; set; } = string.Empty;
}
