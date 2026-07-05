using Inventories.Application.Inventory.Mapping;
using Inventories.Application.Inventory.ReadModels;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Inventories.Application.Inventory.Features.AdjustStock.V1;

/// <summary>Handles <see cref="AdjustStockCommand"/>.</summary>
public static class AdjustStockHandler
{
    /// <summary>
    /// Adjusts a stock item's quantity on hand, commits, then publishes any stock-level integration
    /// events triggered by the adjustment (depletion, replenishment, reorder threshold).
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="bus">The message bus used to publish integration events.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The adjusted stock item.</returns>
    public static async Task<StockItemDto> Handle(
        AdjustStockCommand command,
        IGenericWriteRepository<StockItem, Guid> repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var item = await repository.FirstOrDefaultAsync(new StockItemByIdSpec(command.StockItemId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Stock item '{command.StockItemId}' was not found.");

        bool wasDepleted = item.IsDepleted();
        bool wasBelowReorder = item.CrossedReorderThreshold();

        item.Adjust(command.Delta);

        // Publish integration events only after the commit succeeds. Publishing directly here
        // (rather than via an EF -> Wolverine domain-event bridge, which is not wired platform-wide)
        // mirrors the basket service's CheckoutHandler pattern.
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        if (command.Delta > 0 && wasDepleted && !item.IsDepleted())
        {
            await bus.PublishAsync(new StockReplenishedIntegrationEvent
            {
                ProductId = item.ProductId,
                LocationId = item.LocationId,
                TenantId = item.TenantId,
                Available = item.Available,
            }).ConfigureAwait(false);
        }

        if (!wasDepleted && item.IsDepleted())
        {
            await bus.PublishAsync(new StockDepletedIntegrationEvent
            {
                ProductId = item.ProductId,
                LocationId = item.LocationId,
                TenantId = item.TenantId,
                Available = item.Available,
            }).ConfigureAwait(false);
        }

        // Fire only on the downward crossing into the reorder zone, not on every adjust while
        // already below the threshold — otherwise each subsequent adjustment re-spams the event.
        if (!wasBelowReorder && item.CrossedReorderThreshold())
        {
            await bus.PublishAsync(new ReorderTriggeredIntegrationEvent
            {
                ProductId = item.ProductId,
                LocationId = item.LocationId,
                TenantId = item.TenantId,
                Available = item.Available,
                ReorderThreshold = item.ReorderThreshold,
            }).ConfigureAwait(false);
        }

        return item.ToDto();
    }
}
