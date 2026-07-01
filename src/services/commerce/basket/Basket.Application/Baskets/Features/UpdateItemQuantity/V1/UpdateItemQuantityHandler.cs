using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.UpdateItemQuantity.V1;

/// <summary>Handles <see cref="UpdateItemQuantityCommand"/>.</summary>
public static class UpdateItemQuantityHandler
{
    /// <summary>Updates a line quantity and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated basket.</returns>
    public static async Task<BasketDto> Handle(
        UpdateItemQuantityCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var basket = await repository.FirstOrDefaultAsync(new BasketByIdSpec(command.BasketId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Basket '{command.BasketId}' was not found.");

        basket.UpdateItemQuantity(command.ProductId, command.Quantity);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(basket);
    }
}
