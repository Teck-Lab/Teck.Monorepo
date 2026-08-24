using System.Text.Json;
using Microsoft.Extensions.Options;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Pricing.Application.Pricing.EventHandlers.IntegrationEvents;

/// <summary>Upserts a reconciled catalog fallback and resumes its pending checkout exactly once.</summary>
public static class CatalogPriceReconciledHandler
{
    /// <summary>Stores the response and publishes the resumed pricing outcome.</summary>
    /// <param name="integrationEvent">The catalog reconciliation response.</param>
    /// <param name="catalogPrices">The tracked catalog price repository.</param>
    /// <param name="pendingResolutions">The tracked pending-resolution repository.</param>
    /// <param name="prices">The active price-list repository.</param>
    /// <param name="exchangeRates">The exchange-rate repository.</param>
    /// <param name="catalogPriceReads">The catalog fallback read repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="options">The pricing options.</param>
    /// <param name="bus">The message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the reconciliation.</returns>
    public static async Task Handle(
        CatalogPriceReconciledIntegrationEvent integrationEvent,
        IGenericWriteRepository<CatalogPrice, Guid> catalogPrices,
        IGenericWriteRepository<PendingPriceResolution, Guid> pendingResolutions,
        IGenericReadRepository<Price, Guid> prices,
        IGenericReadRepository<ExchangeRate, Guid> exchangeRates,
        IGenericReadRepository<CatalogPrice, Guid> catalogPriceReads,
        IUnitOfWork unitOfWork,
        IOptions<PricingOptions> options,
        IMessageBus bus,
        CancellationToken ct)
    {
        var pending = await pendingResolutions
            .FirstOrDefaultAsync(new PendingPriceResolutionByRequestSpec(integrationEvent.RequestId, integrationEvent.TenantId), enableTracking: true, ct)
            .ConfigureAwait(false);
        if (pending is null || pending.IsResolved || pending.ProductId != integrationEvent.ProductId)
        {
            return;
        }

        var existing = await catalogPrices
            .FirstOrDefaultAsync(new CatalogPriceByProductSpec(integrationEvent.ProductId, integrationEvent.TenantId), enableTracking: true, ct)
            .ConfigureAwait(false);
        var fallback = existing ?? CatalogPrice.Create(
            integrationEvent.ProductId,
            integrationEvent.VariantId,
            integrationEvent.Amount,
            integrationEvent.Currency,
            pending.CreatedAt,
            integrationEvent.TenantId);
        if (existing is null)
        {
            await catalogPrices.AddAsync(fallback, ct).ConfigureAwait(false);
        }
        else if (existing.ChangedAt <= pending.CreatedAt)
        {
            existing.Update(integrationEvent.VariantId, integrationEvent.Amount, integrationEvent.Currency, pending.CreatedAt);
        }

        var lines = JsonSerializer.Deserialize<List<BasketCheckoutRequestedLine>>(pending.LinesJson) ?? [];
        var request = new BasketCheckoutRequestedIntegrationEvent
        {
            BasketId = pending.BasketId,
            TenantId = pending.TenantId,
            AuthorizedAmount = pending.AuthorizedAmount,
            Currency = pending.Currency,
            RequestId = pending.RequestId,
            SourceCorrelationId = pending.SourceCorrelationId,
            Lines = lines,
        };
        var resolution = await BasketCheckoutRequestedHandler.TryPriceAsync(
            request,
            prices,
            exchangeRates,
            catalogPriceReads,
            options,
            fallback,
            ct).ConfigureAwait(false);

        if (resolution.MissingProductId is Guid missingProductId)
        {
            pending.AwaitProduct(missingProductId);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            await bus.PublishAsync(new CatalogPriceReconciliationRequestedIntegrationEvent
            {
                ProductId = missingProductId,
                TenantId = pending.TenantId,
                RequestId = pending.RequestId,
                SourceCorrelationId = pending.SourceCorrelationId,
            }).ConfigureAwait(false);
            return;
        }

        pending.MarkResolved();
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        if (resolution.Lines is not null)
        {
            await bus.PublishAsync(BasketCheckoutRequestedHandler.Priced(request, resolution.Lines)).ConfigureAwait(false);
        }
        else
        {
            await bus.PublishAsync(BasketCheckoutRequestedHandler.Failed(request, resolution.FailureCategory ?? "price-unavailable")).ConfigureAwait(false);
        }
    }
}
