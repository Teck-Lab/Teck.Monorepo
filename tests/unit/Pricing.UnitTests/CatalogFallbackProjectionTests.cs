using Microsoft.Extensions.Options;
using NSubstitute;
using Pricing.Application.Pricing;
using Pricing.Application.Pricing.EventHandlers.IntegrationEvents;
using Pricing.Application.Pricing.Features.ResolvePrice.V1;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Pricing.UnitTests;

/// <summary>Locks the catalog-fallback projection's precedence and stale-delivery behavior.</summary>
public sealed class CatalogFallbackProjectionTests
{
    [Fact]
    public async Task ReconciliationResponse_DoesNotOverwriteProjectionNewerThanPendingRequest()
    {
        Guid productId = Guid.NewGuid();
        var pending = Pending(productId, "stale-response-request");
        var newerProjection = CatalogPrice.Create(productId, Guid.NewGuid(), 20m, "USD", DateTimeOffset.UtcNow.AddMinutes(5), "tenant-a");
        var catalogWrites = Substitute.For<IGenericWriteRepository<CatalogPrice, Guid>>();
        var pendingWrites = Substitute.For<IGenericWriteRepository<PendingPriceResolution, Guid>>();
        var reads = Substitute.For<IGenericReadRepository<CatalogPrice, Guid>>();
        var prices = Substitute.For<IGenericReadRepository<Price, Guid>>();
        var rates = Substitute.For<IGenericReadRepository<ExchangeRate, Guid>>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();

        catalogWrites.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<CatalogPrice>>(), true, Arg.Any<CancellationToken>()).Returns(newerProjection);
        pendingWrites.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<PendingPriceResolution>>(), true, Arg.Any<CancellationToken>()).Returns(pending);
        reads.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<CatalogPrice>>(), Arg.Any<CancellationToken>()).Returns(newerProjection);
        prices.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Price>>(), Arg.Any<CancellationToken>()).Returns([]);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        await CatalogPriceReconciledHandler.Handle(Response(productId, pending.RequestId, 10m), catalogWrites, pendingWrites, prices, rates, reads, unitOfWork, Options.Create(new PricingOptions()), bus, CancellationToken.None);
        await CatalogPriceReconciledHandler.Handle(Response(productId, pending.RequestId, 10m), catalogWrites, pendingWrites, prices, rates, reads, unitOfWork, Options.Create(new PricingOptions()), bus, CancellationToken.None);

        Assert.Equal(20m, newerProjection.Amount);
        await bus.Received(1).PublishAsync(Arg.Is<BasketPricedIntegrationEvent>(message => message.Amount == 20m));
    }

    [Fact]
    public async Task Resolve_ActivePriceListPrecedesCatalogFallback()
    {
        Guid productId = Guid.NewGuid();
        var list = PriceList.Create("Retail", new PriceScope("USD", null, null, null), null, null, "tenant-a");
        list.AddOrUpdatePrice(productId, new Money(15m, "USD"), []);
        list.Activate();
        Price price = Assert.Single(list.Prices);
        typeof(Price).GetProperty(nameof(Price.PriceList))!.SetValue(price, list);
        var prices = Substitute.For<IGenericReadRepository<Price, Guid>>();
        var rates = Substitute.For<IGenericReadRepository<ExchangeRate, Guid>>();
        var fallbacks = Substitute.For<IGenericReadRepository<CatalogPrice, Guid>>();
        prices.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Price>>(), Arg.Any<CancellationToken>()).Returns([price]);
        fallbacks.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<CatalogPrice>>(), Arg.Any<CancellationToken>())
            .Returns(CatalogPrice.Create(productId, Guid.NewGuid(), 9m, "USD", DateTimeOffset.UtcNow, "tenant-a"));

        var result = await ResolvePriceHandler.ResolveAsync(new ResolvePriceQuery(productId, "USD", 1, null, null, null, null), prices, rates, fallbacks, Options.Create(new PricingOptions()), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(15m, result.Value.UnitAmount);
    }

    private static PendingPriceResolution Pending(Guid productId, string requestId) => PendingPriceResolution.Create(
        productId, Guid.NewGuid(), 100m, "USD", requestId, requestId,
        "[{\"ProductId\":\"" + productId + "\",\"Quantity\":1}]", "tenant-a");

    private static CatalogPriceReconciledIntegrationEvent Response(Guid productId, string requestId, decimal amount) => new()
    {
        ProductId = productId,
        VariantId = Guid.NewGuid(),
        TenantId = "tenant-a",
        RequestId = requestId,
        SourceCorrelationId = requestId,
        Amount = amount,
        Currency = "USD",
    };
}
