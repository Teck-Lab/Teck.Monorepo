using Baskets.Application.Baskets.Mapping;
using Baskets.Application.Baskets.ReadModels;
using Baskets.Application.Baskets.Responses;
using Baskets.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Baskets.Application.Baskets.Features.Checkout.V1;

/// <summary>Handles <see cref="CheckoutCommand"/>.</summary>
public static class CheckoutHandler
{
    /// <summary>
    /// Starts authoritative pricing for the caller's basket and publishes no caller-supplied price.
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

        if (string.IsNullOrWhiteSpace(identity.Subject))
        {
            throw new UnauthorizedAccessException("Checkout requires an authenticated shopper subject.");
        }

        BasketOwnership.EnsureOwnedBy(basket, identity);

        basket.BeginCheckout(command.AuthorizedAmount, command.Currency, command.PaymentReference);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new BasketCheckoutRequestedIntegrationEvent
        {
            BasketId = basket.Id,
            TenantId = basket.TenantId,
            AuthorizedAmount = basket.AuthorizedAmount,
            Currency = basket.Currency!,
            RequestId = basket.CheckoutRequestId!,
            SourceCorrelationId = basket.Id.ToString("N"),
            Lines = basket.Items
                .Select(item => new BasketCheckoutRequestedLine { ProductId = item.ProductId, Quantity = item.Quantity })
                .ToList(),
        }).ConfigureAwait(false);

        return BasketMapper.ToDto(basket);
    }
}
