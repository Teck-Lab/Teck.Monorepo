using ErrorOr;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using Wolverine;

namespace Pricing.Application.Pricing.Features.RemovePrice.V1;

/// <summary>Handles <see cref="RemovePriceCommand"/>.</summary>
public static class RemovePriceHandler
{
    /// <summary>Loads the list, removes the product's price, commits, and publishes effective changes.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated list, or a not-found error.</returns>
    public static async Task<ErrorOr<PriceListDto>> Handle(
        RemovePriceCommand command,
        IGenericWriteRepository<PriceList, Guid> repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        PriceList? list = await repository.FirstOrDefaultAsync(new PriceListByIdSpec(command.PriceListId), enableTracking: true, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Error.NotFound(description: $"Price list '{command.PriceListId}' was not found.");
        }

        list.RemovePrice(command.ProductId);

        var events = list.DomainEvents.OfType<PriceChanged>().ToList();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await PricingEventPublisher.PublishAsync(events, bus).ConfigureAwait(false);
        return list.ToDto();
    }
}
