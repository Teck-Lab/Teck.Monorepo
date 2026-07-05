using Inventories.Application.Inventory.Mapping;
using Inventories.Application.Inventory.ReadModels;
using Inventories.Application.Inventory.Responses;
using Inventories.Domain.Entities;
using SharedKernel.Core.Database;

namespace Inventories.Application.Inventory.Features.SetPolicy.V1;

/// <summary>Handles <see cref="SetPolicyCommand"/>.</summary>
public static class SetPolicyHandler
{
    /// <summary>Updates a stock item's backorder and reorder-threshold policy and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated stock item.</returns>
    public static async Task<StockItemDto> Handle(
        SetPolicyCommand command,
        IGenericWriteRepository<StockItem, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var item = await repository.FirstOrDefaultAsync(new StockItemByIdSpec(command.StockItemId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Stock item '{command.StockItemId}' was not found.");

        item.SetPolicy(command.AllowBackorder, command.ReorderThreshold);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return item.ToDto();
    }
}
