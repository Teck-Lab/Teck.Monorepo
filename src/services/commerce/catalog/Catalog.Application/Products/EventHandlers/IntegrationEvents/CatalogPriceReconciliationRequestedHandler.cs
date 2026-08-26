using Catalog.Application.Products.ReadModels;
using Catalog.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Catalog.Application.Products.EventHandlers.IntegrationEvents;

/// <summary>Answers a pricing reconciliation request from the current default variant.</summary>
public static class CatalogPriceReconciliationRequestedHandler
{
    /// <summary>Reads the tenant-scoped product once and publishes its current default sell price.</summary>
    /// <param name="integrationEvent">The reconciliation request.</param>
    /// <param name="products">The catalog product read repository.</param>
    /// <param name="bus">The message bus used to publish the response.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task representing the asynchronous response.</returns>
    public static async Task Handle(
        CatalogPriceReconciliationRequestedIntegrationEvent integrationEvent,
        IGenericReadRepository<Product, Guid> products,
        IMessageBus bus,
        CancellationToken ct)
    {
        var product = await products
            .FirstOrDefaultAsync(new ProductByIdSpec(integrationEvent.ProductId), ct)
            .ConfigureAwait(false);

        var defaultVariant = product?.Variants.SingleOrDefault(variant => variant.IsDefault);
        if (defaultVariant is null)
        {
            return;
        }

        await bus.PublishAsync(new CatalogPriceReconciledIntegrationEvent
        {
            ProductId = product!.Id,
            VariantId = defaultVariant.Id,
            TenantId = product.TenantId,
            Amount = defaultVariant.SellPrice.Amount,
            Currency = defaultVariant.SellPrice.Currency,
            RequestId = integrationEvent.RequestId,
            SourceCorrelationId = integrationEvent.SourceCorrelationId,
        }).ConfigureAwait(false);
    }
}
