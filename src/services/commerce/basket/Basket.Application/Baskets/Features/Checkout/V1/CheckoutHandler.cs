using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.Checkout.V1;

/// <summary>Handles <see cref="CheckoutCommand"/>.</summary>
public static class CheckoutHandler
{
    /// <summary>Checks out the basket (raising the domain event) and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The checked-out basket.</returns>
    public static async Task<BasketDto> Handle(
        CheckoutCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var basket = await repository.FirstOrDefaultAsync(new BasketByIdSpec(command.BasketId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Basket '{command.BasketId}' was not found.");

        basket.Checkout();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return BasketMapper.ToDto(basket);
    }
}
