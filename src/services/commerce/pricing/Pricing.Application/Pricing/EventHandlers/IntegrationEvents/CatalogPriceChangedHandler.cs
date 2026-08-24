using Pricing.Application.Pricing.ReadModels;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;

namespace Pricing.Application.Pricing.EventHandlers.IntegrationEvents;

/// <summary>Projects asynchronous catalog sell-price changes into the pricing fallback table.</summary>
public static class CatalogPriceChangedHandler
{
    /// <summary>Idempotently upserts the tenant-scoped catalog fallback price.</summary>
    /// <param name="integrationEvent">The catalog price change.</param>
    /// <param name="catalogPrices">The tracked catalog price repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the projection.</returns>
    public static async Task Handle(
        CatalogPriceChangedIntegrationEvent integrationEvent,
        IGenericWriteRepository<CatalogPrice, Guid> catalogPrices,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var existing = await catalogPrices
            .FirstOrDefaultAsync(new CatalogPriceByProductSpec(integrationEvent.ProductId, integrationEvent.TenantId), enableTracking: true, ct)
            .ConfigureAwait(false);

        if (existing is null)
        {
            await catalogPrices.AddAsync(
                CatalogPrice.Create(
                    integrationEvent.ProductId,
                    integrationEvent.VariantId,
                    integrationEvent.Amount,
                    integrationEvent.Currency,
                    integrationEvent.ChangedAt,
                    integrationEvent.TenantId),
                ct).ConfigureAwait(false);
        }
        else
        {
            existing.Update(
                integrationEvent.VariantId,
                integrationEvent.Amount,
                integrationEvent.Currency,
                integrationEvent.ChangedAt);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
