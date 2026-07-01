using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using SharedKernel.Core.Database;

namespace Baskets.Application.Baskets.Features.GetOrCreateBasket.V1;

/// <summary>Handles <see cref="GetOrCreateBasketCommand"/> with get-or-create semantics.</summary>
public static class GetOrCreateBasketHandler
{
    /// <summary>Returns the caller's active basket, creating and committing a new one on miss.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="identity">The basket identity accessor.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The active basket as a <see cref="BasketDto"/>.</returns>
    public static async Task<BasketDto> Handle(
        GetOrCreateBasketCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IUnitOfWork unitOfWork,
        IBasketIdentityAccessor identity,
        ITenantInfo tenant,
        CancellationToken ct)
    {
        Basket? basket = identity.CustomerId is Guid customerId
            ? await repository.FirstOrDefaultAsync(new ActiveBasketByCustomerSpec(customerId), enableTracking: true, ct).ConfigureAwait(false)
            : await repository.FirstOrDefaultAsync(new ActiveBasketByTokenSpec(identity.EnsureAnonymousToken()), enableTracking: true, ct).ConfigureAwait(false);

        if (basket is null)
        {
            basket = identity.CustomerId is Guid ownerId
                ? Basket.CreateForCustomer(ownerId, tenant.Id ?? string.Empty)
                : Basket.CreateAnonymous(identity.EnsureAnonymousToken(), tenant.Id ?? string.Empty);

            await repository.AddAsync(basket, ct).ConfigureAwait(false);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        }

        return BasketMapper.ToDto(basket);
    }
}
