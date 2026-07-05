using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.RemoveItem.V1;

/// <summary>Handles <see cref="RemoveItemCommand"/>.</summary>
public static class RemoveItemHandler
{
    /// <summary>Removes a line and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="identity">The current caller identity (used to enforce basket ownership).</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated basket.</returns>
    public static async Task<BasketDto> Handle(
        RemoveItemCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IBasketIdentityAccessor identity,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var basket = await repository.FirstOrDefaultAsync(new BasketByIdSpec(command.BasketId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Basket '{command.BasketId}' was not found.");

        BasketOwnership.EnsureOwnedBy(basket, identity);

        basket.RemoveItem(command.ProductId);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(basket);
    }
}
