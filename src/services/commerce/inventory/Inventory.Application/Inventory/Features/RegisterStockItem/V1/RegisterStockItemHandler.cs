using Finbuckle.MultiTenant.Abstractions;
using Inventories.Application.Inventory.Mapping;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using SharedKernel.Core.Database;

namespace Inventories.Application.Inventory.Features.RegisterStockItem.V1;

/// <summary>Handles <see cref="RegisterStockItemCommand"/>.</summary>
public static class RegisterStockItemHandler
{
    /// <summary>Registers a new stock item for a product at a location and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="tenant">The current tenant, used to stamp the new stock item.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The registered stock item.</returns>
    public static async Task<StockItemDto> Handle(
        RegisterStockItemCommand command,
        IGenericWriteRepository<StockItem, Guid> repository,
        IUnitOfWork unitOfWork,
        ITenantInfo tenant,
        CancellationToken ct)
    {
        var item = StockItem.Create(
            command.ProductId,
            command.LocationId,
            tenant.Id ?? string.Empty,
            command.QuantityOnHand,
            command.AllowBackorder,
            command.ReorderThreshold);

        await repository.AddAsync(item, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return item.ToDto();
    }
}
