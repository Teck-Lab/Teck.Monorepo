using Ardalis.Specification;
using Catalog.Application.Products.EventHandlers.IntegrationEvents;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using NSubstitute;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CatalogPriceReconciliationRequestedHandlerTests
{
    [Fact]
    public async Task Handle_DefaultVariant_PublishesCurrentTenantScopedSellPrice()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(12.50m, "USD"));
        var products = Substitute.For<IGenericReadRepository<Product, Guid>>();
        products.FirstOrDefaultAsync(Arg.Any<ISpecification<Product>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Product?>(product));
        var bus = Substitute.For<IMessageBus>();

        await CatalogPriceReconciliationRequestedHandler.Handle(new CatalogPriceReconciliationRequestedIntegrationEvent
        {
            ProductId = product.Id,
            TenantId = "tenant-1",
            RequestId = "price-request",
            SourceCorrelationId = "checkout-correlation",
        }, products, bus, CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Is<CatalogPriceReconciledIntegrationEvent>(result =>
            result.ProductId == product.Id &&
            result.Amount == 12.50m &&
            result.Currency == "USD" &&
            result.RequestId == "price-request"));
    }
}
