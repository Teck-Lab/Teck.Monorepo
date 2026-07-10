using ErrorOr;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using Wolverine;

namespace Pricing.Application.Pricing.Features.ActivatePriceList.V1;

/// <summary>Handles <see cref="ActivatePriceListCommand"/>.</summary>
public static class ActivatePriceListHandler
{
    /// <summary>Loads, activates, commits, and publishes an Upserted event per price.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The activated list, or a not-found error.</returns>
    public static async Task<ErrorOr<PriceListDto>> Handle(
        ActivatePriceListCommand command,
        IGenericWriteRepository<PriceList, Guid> repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        PriceList? list = await repository.FirstOrDefaultAsync(new PriceListByIdSpec(command.Id), enableTracking: true, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Error.NotFound(description: $"Price list '{command.Id}' was not found.");
        }

        list.Activate();
        var events = list.DomainEvents.OfType<PriceChanged>().ToList();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await PricingEventPublisher.PublishAsync(events, bus).ConfigureAwait(false);
        return list.ToDto();
    }
}
