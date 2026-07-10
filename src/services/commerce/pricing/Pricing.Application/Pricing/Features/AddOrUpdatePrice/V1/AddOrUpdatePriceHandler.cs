using ErrorOr;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.DomainEvents;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using Wolverine;

namespace Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;

/// <summary>Handles <see cref="AddOrUpdatePriceCommand"/>.</summary>
public static class AddOrUpdatePriceHandler
{
    /// <summary>Loads the list, upserts the product's price, commits, and publishes effective changes.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository for the owning price list.</param>
    /// <param name="priceRepository">The write repository for prices, used to explicitly track brand-new prices.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The updated list, or a not-found error.</returns>
    public static async Task<ErrorOr<PriceListDto>> Handle(
        AddOrUpdatePriceCommand command,
        IGenericWriteRepository<PriceList, Guid> repository,
        IGenericWriteRepository<Price, Guid> priceRepository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        PriceList? list = await repository.FirstOrDefaultAsync(new PriceListByIdSpec(command.PriceListId), enableTracking: true, ct).ConfigureAwait(false);
        if (list is null)
        {
            return Error.NotFound(description: $"Price list '{command.PriceListId}' was not found.");
        }

        if (list.Status == PriceListStatus.Archived)
        {
            return Error.Conflict(description: $"Price list '{list.Id}' is archived and cannot be modified.");
        }

        string currency = list.Scope.Currency;
        var amount = new Money(command.Amount, currency);
        var tiers = command.Tiers
            .Select(tier => new PriceTier(tier.MinQuantity, new Money(tier.Amount, currency)))
            .ToList();

        // Price has its own identity, primary key, and DbSet (a first-class entity read directly on the
        // resolution hot path — see PricesByProductSpec) rather than an EF owned type. Its Id is assigned
        // client-side (BaseEntity's constructor) before it is ever added to the tracked PriceList.Prices
        // collection, so a brand-new Price reached only through PriceList's navigation fix-up already has a
        // non-default key: EF Core's change-tracker cannot distinguish that from "an existing row being
        // reattached" and marks it Modified instead of Added, producing an UPDATE that always affects 0 rows
        // (DbUpdateConcurrencyException). Explicitly tracking genuinely new prices via AddAsync — exactly like
        // CreatePriceListHandler/SetExchangeRateHandler already do for their own root entities — sidesteps that
        // ambiguity entirely.
        bool isNewPrice = list.Prices.All(price => price.ProductId != command.ProductId);

        list.AddOrUpdatePrice(command.ProductId, amount, tiers);

        if (isNewPrice)
        {
            Price added = list.Prices.First(price => price.ProductId == command.ProductId);
            await priceRepository.AddAsync(added, ct).ConfigureAwait(false);
        }

        var events = list.DomainEvents.OfType<PriceChanged>().ToList();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        await PricingEventPublisher.PublishAsync(events, bus).ConfigureAwait(false);
        return list.ToDto();
    }
}
