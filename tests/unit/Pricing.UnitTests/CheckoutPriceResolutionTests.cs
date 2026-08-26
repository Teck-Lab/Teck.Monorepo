using System.Text.Json;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pricing.Application.Pricing;
using Pricing.Application.Pricing.EventHandlers.IntegrationEvents;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Pricing.UnitTests;

/// <summary>Locks checkout pricing's bounded multi-product catalog reconciliation flow.</summary>
public sealed class CheckoutPriceResolutionTests
{
    [Fact]
    public async Task Checkout_WithCatalogFallback_PublishesAuthoritativePricedResult()
    {
        Guid productId = Guid.NewGuid();
        var request = Request(productId);
        var prices = Substitute.For<IGenericReadRepository<Price, Guid>>();
        var rates = Substitute.For<IGenericReadRepository<ExchangeRate, Guid>>();
        var fallbacks = Substitute.For<IGenericReadRepository<CatalogPrice, Guid>>();
        var pending = Substitute.For<IGenericWriteRepository<PendingPriceResolution, Guid>>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        prices.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Price>>(), Arg.Any<CancellationToken>()).Returns([]);
        fallbacks.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<CatalogPrice>>(), Arg.Any<CancellationToken>())
            .Returns(CatalogPrice.Create(productId, Guid.NewGuid(), 10m, "USD", DateTimeOffset.UtcNow, request.TenantId));

        await BasketCheckoutRequestedHandler.Handle(
            request,
            prices,
            rates,
            fallbacks,
            pending,
            unitOfWork,
            Options.Create(new PricingOptions()),
            bus,
            CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Is<BasketPricedIntegrationEvent>(message =>
            message.RequestId == request.RequestId && message.Amount == 10m && Assert.Single(message.Lines).UnitPrice == 10m));
    }

    [Fact]
    public async Task Checkout_AboveAuthorizationCeiling_PublishesFailureInsteadOfPricedResult()
    {
        Guid productId = Guid.NewGuid();
        var request = Request(productId, authorizedAmount: 9m);
        var prices = Substitute.For<IGenericReadRepository<Price, Guid>>();
        var rates = Substitute.For<IGenericReadRepository<ExchangeRate, Guid>>();
        var fallbacks = Substitute.For<IGenericReadRepository<CatalogPrice, Guid>>();
        var pending = Substitute.For<IGenericWriteRepository<PendingPriceResolution, Guid>>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        prices.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Price>>(), Arg.Any<CancellationToken>()).Returns([]);
        fallbacks.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<CatalogPrice>>(), Arg.Any<CancellationToken>())
            .Returns(CatalogPrice.Create(productId, Guid.NewGuid(), 10m, "USD", DateTimeOffset.UtcNow, request.TenantId));

        await BasketCheckoutRequestedHandler.Handle(
            request,
            prices,
            rates,
            fallbacks,
            pending,
            unitOfWork,
            Options.Create(new PricingOptions()),
            bus,
            CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Is<BasketPricingFailedIntegrationEvent>(message =>
            message.RequestId == request.RequestId && message.FailureCategory == "authorization-exceeded"));
        await bus.DidNotReceive().PublishAsync(Arg.Any<BasketPricedIntegrationEvent>());
    }

    [Fact]
    public async Task Reconciliation_TwoUncoveredProducts_RedeliveryAdvancesThenPublishesOnePricedResult()
    {
        Guid first = Guid.NewGuid();
        Guid second = Guid.NewGuid();
        var request = Request(first, second);
        var pending = PendingPriceResolution.Create(first, request.BasketId, request.AuthorizedAmount, request.Currency, request.RequestId, request.SourceCorrelationId, JsonSerializer.Serialize(request.Lines), request.TenantId);
        var catalogWrites = Substitute.For<IGenericWriteRepository<CatalogPrice, Guid>>();
        var pendingWrites = Substitute.For<IGenericWriteRepository<PendingPriceResolution, Guid>>();
        var catalogReads = Substitute.For<IGenericReadRepository<CatalogPrice, Guid>>();
        var prices = Substitute.For<IGenericReadRepository<Price, Guid>>();
        var rates = Substitute.For<IGenericReadRepository<ExchangeRate, Guid>>();
        var unitOfWork = Substitute.For<IUnitOfWork>();
        var bus = Substitute.For<IMessageBus>();
        var firstFallback = CatalogPrice.Create(first, Guid.NewGuid(), 10m, "USD", pending.CreatedAt, request.TenantId);

        pendingWrites.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<PendingPriceResolution>>(), true, Arg.Any<CancellationToken>()).Returns(pending);
        catalogWrites.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<CatalogPrice>>(), true, Arg.Any<CancellationToken>()).Returns((CatalogPrice?)null, (CatalogPrice?)null, (CatalogPrice?)null);
        catalogReads.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<CatalogPrice>>(), Arg.Any<CancellationToken>()).Returns((CatalogPrice?)null, (CatalogPrice?)null, firstFallback, (CatalogPrice?)null);
        prices.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Price>>(), Arg.Any<CancellationToken>()).Returns([]);
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(1);

        await CatalogPriceReconciledHandler.Handle(Response(first, request, 10m), catalogWrites, pendingWrites, prices, rates, catalogReads, unitOfWork, Options.Create(new PricingOptions()), bus, CancellationToken.None);
        await CatalogPriceReconciledHandler.Handle(Response(second, request, 12m), catalogWrites, pendingWrites, prices, rates, catalogReads, unitOfWork, Options.Create(new PricingOptions()), bus, CancellationToken.None);
        await CatalogPriceReconciledHandler.Handle(Response(second, request, 12m), catalogWrites, pendingWrites, prices, rates, catalogReads, unitOfWork, Options.Create(new PricingOptions()), bus, CancellationToken.None);

        Assert.True(pending.IsResolved);
        await bus.Received(1).PublishAsync(Arg.Is<CatalogPriceReconciliationRequestedIntegrationEvent>(message => message.ProductId == second && message.RequestId == request.RequestId));
        await bus.Received(1).PublishAsync(Arg.Is<BasketPricedIntegrationEvent>(message =>
            message.RequestId == request.RequestId && message.Amount == 22m && message.Lines.Count == 2));
    }

    private static BasketCheckoutRequestedIntegrationEvent Request(Guid first, Guid? second = null, decimal authorizedAmount = 100m) => new()
    {
        BasketId = Guid.NewGuid(),
        TenantId = "tenant-a",
        AuthorizedAmount = authorizedAmount,
        Currency = "USD",
        RequestId = "two-product-request",
        SourceCorrelationId = "two-product-request",
        Lines = second is Guid secondProduct
            ? [new BasketCheckoutRequestedLine { ProductId = first, Quantity = 1 }, new BasketCheckoutRequestedLine { ProductId = secondProduct, Quantity = 1 }]
            : [new BasketCheckoutRequestedLine { ProductId = first, Quantity = 1 }],
    };

    private static CatalogPriceReconciledIntegrationEvent Response(Guid productId, BasketCheckoutRequestedIntegrationEvent request, decimal amount) => new()
    {
        ProductId = productId,
        VariantId = Guid.NewGuid(),
        TenantId = request.TenantId,
        RequestId = request.RequestId,
        SourceCorrelationId = request.SourceCorrelationId,
        Amount = amount,
        Currency = request.Currency,
    };
}
