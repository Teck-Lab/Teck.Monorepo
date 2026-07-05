using Finbuckle.MultiTenant.Abstractions;
using Inventories.Application.Inventory.Mapping;
using Inventories.Application.Inventory.ReadModels;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using SharedKernel.Core.Database;

namespace Inventories.Application.Inventory.Features.SetLocationPriorities.V1;

/// <summary>Handles <see cref="SetLocationPrioritiesCommand"/>.</summary>
public static class SetLocationPrioritiesHandler
{
    /// <summary>Upserts the tenant's ordered location priority list and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The persisted location priority list.</returns>
    public static async Task<LocationPriorityDto> Handle(
        SetLocationPrioritiesCommand command,
        IGenericWriteRepository<LocationPriority, Guid> repository,
        IUnitOfWork unitOfWork,
        ITenantInfo tenant,
        CancellationToken ct)
    {
        var tenantId = tenant.Id ?? string.Empty;
        var priority = await repository.FirstOrDefaultAsync(new LocationPriorityByTenantSpec(tenantId), enableTracking: true, ct).ConfigureAwait(false);

        if (priority is null)
        {
            priority = LocationPriority.Create(tenantId, command.LocationIds);
            await repository.AddAsync(priority, ct).ConfigureAwait(false);
        }
        else
        {
            priority.Set(command.LocationIds);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return priority.ToDto();
    }
}
