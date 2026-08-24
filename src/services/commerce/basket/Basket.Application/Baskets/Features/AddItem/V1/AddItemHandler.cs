using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.AddItem.V1;

/// <summary>Handles <see cref="AddItemCommand"/>.</summary>
public static class AddItemHandler
{
    /// <summary>Adds an item to the basket and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="identity">The current caller identity (used to enforce basket ownership).</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated basket.</returns>
    public static async Task<BasketDto> Handle(
        AddItemCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IBasketIdentityAccessor identity,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var basket = await repository.FirstOrDefaultAsync(new BasketByIdSpec(command.BasketId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Basket '{command.BasketId}' was not found.");

        BasketOwnership.EnsureOwnedBy(basket, identity);

        basket.AddItem(command.ProductId, command.ProductName, command.Quantity);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(basket);
    }
}
