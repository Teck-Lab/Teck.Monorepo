using Customers.Application.Tenants.ReadModels;
using Customers.Domain.Entities;
using FastEndpoints;
using SharedKernel.Core.Database;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;

namespace Customers.Host.Grpc.V1;

/// <summary>Handles remote tenant database-metadata lookups for the gateway.</summary>
/// <param name="repository">The generic tenant read repository.</param>
public sealed class GetTenantDatabaseInfoCommandHandler(IGenericReadRepository<Tenant, Guid> repository)
    : ICommandHandler<GetTenantDatabaseInfoCommand, TenantDatabaseInfoRpcResult>
{
    private readonly IGenericReadRepository<Tenant, Guid> repository = repository;

    /// <inheritdoc/>
    public async Task<TenantDatabaseInfoRpcResult> ExecuteAsync(GetTenantDatabaseInfoCommand command, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(command);

        if (!Guid.TryParse(command.TenantId, out Guid tenantId))
        {
            return new TenantDatabaseInfoRpcResult { Found = false, ErrorDetail = "tenant_id must be a valid GUID." };
        }

        Tenant? tenant = await repository.FirstOrDefaultAsync(new TenantByIdSpec(tenantId), ct).ConfigureAwait(false);

        if (tenant is null)
        {
            return new TenantDatabaseInfoRpcResult
            {
                Found = false,
                TenantId = command.TenantId,
                ErrorDetail = $"Tenant '{command.TenantId}' was not found.",
            };
        }

        return new TenantDatabaseInfoRpcResult
        {
            Found = true,
            TenantId = tenant.Id.ToString(),
            Identifier = tenant.Identifier,
            DatabaseStrategy = tenant.DatabaseStrategy,
            DatabaseProvider = tenant.DatabaseProvider,
            HasReadReplicas = tenant.HasReadReplicas,
        };
    }
}
