using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.DomainEvents;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Baskets.Application.Baskets.Features.Checkout.V1;

/// <summary>Handles <see cref="CheckoutCommand"/>.</summary>
public static class CheckoutHandler
{
    /// <summary>
    /// Checks out the caller's basket, commits, then publishes the
    /// <see cref="BasketCheckedOutIntegrationEvent"/> that the order service consumes.
    /// </summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="identity">The current caller identity (used to enforce basket ownership).</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="bus">The message bus used to publish the integration event.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The checked-out basket.</returns>
    public static async Task<BasketDto> Handle(
        CheckoutCommand command,
        IGenericWriteRepository<Basket, Guid> repository,
        IBasketIdentityAccessor identity,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var basket = await repository.FirstOrDefaultAsync(new BasketByIdSpec(command.BasketId), enableTracking: true, ct).ConfigureAwait(false)
            ?? throw new InvalidOperationException($"Basket '{command.BasketId}' was not found.");

        BasketOwnership.EnsureOwnedBy(basket, identity);

        basket.Checkout();

        // Capture the domain event before commit; publish the integration event only after the
        // commit succeeds. Publishing directly here (rather than via an EF -> Wolverine domain-event
        // bridge, which is not wired platform-wide) mirrors the order service's working pattern.
        var checkedOut = basket.DomainEvents.OfType<BasketCheckedOut>().Single();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new BasketCheckedOutIntegrationEvent
        {
            BasketId = checkedOut.BasketId,
            CustomerId = checkedOut.CustomerId,
            TenantId = checkedOut.TenantId,
            Subtotal = checkedOut.Subtotal,
            CheckedOutAt = checkedOut.CheckedOutAt,
            Items = checkedOut.Items
                .Select(item => new BasketCheckedOutLine(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity, item.LineTotal))
                .ToList(),
        }).ConfigureAwait(false);

        return BasketMapper.ToDto(basket);
    }
}
