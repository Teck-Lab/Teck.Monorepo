using ErrorOr;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pricing.Application.Pricing;
using Pricing.Application.Pricing.Features.ResolvePrice.V1;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;
using SharedKernel.Core.Database;
using Xunit;

namespace Pricing.UnitTests;

public sealed class ResolvePriceHandlerTests
{
    private static readonly Guid Product = Guid.NewGuid();

    private static Price ActivePrice(PriceScope scope, decimal amount)
    {
        var list = PriceList.Create("l", scope, null, null, "tenant-1");
        list.AddOrUpdatePrice(Product, new Money(amount, scope.Currency), []);
        list.Activate();
        Price price = System.Linq.Enumerable.Single(list.Prices);
        typeof(Price).GetProperty(nameof(Price.PriceList))!.SetValue(price, list);
        return price;
    }

    private static (IGenericReadRepository<Price, Guid> Prices, IGenericReadRepository<ExchangeRate, Guid> Rates) Repos(
        IReadOnlyList<Price> prices, ExchangeRate? rate = null)
    {
        var priceRepo = Substitute.For<IGenericReadRepository<Price, Guid>>();
        priceRepo.ListAsync(Arg.Any<Ardalis.Specification.ISpecification<Price>>(), Arg.Any<CancellationToken>()).Returns(prices);
        var rateRepo = Substitute.For<IGenericReadRepository<ExchangeRate, Guid>>();
        rateRepo.FirstOrDefaultAsync(Arg.Any<Ardalis.Specification.ISpecification<ExchangeRate>>(), Arg.Any<CancellationToken>()).Returns(rate);
        return (priceRepo, rateRepo);
    }

    private static IOptions<PricingOptions> Options() => Microsoft.Extensions.Options.Options.Create(new PricingOptions());

    [Fact]
    public async Task Resolve_NativeCurrency_ReturnsUnconverted()
    {
        var price = ActivePrice(new PriceScope("USD", null, null, null), 10m);
        var (prices, rates) = Repos([price]);

        var result = await ResolvePriceHandler.Handle(
            new ResolvePriceQuery(Product, "USD", 1, null, null, null, null), prices, rates, Options(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.False(result.Value.Converted);
        Assert.Equal(10m, result.Value.UnitAmount);
    }

    [Fact]
    public async Task Resolve_NoPrice_ReturnsNotFound()
    {
        var (prices, rates) = Repos([]);

        var result = await ResolvePriceHandler.Handle(
            new ResolvePriceQuery(Product, "USD", 1, null, null, null, null), prices, rates, Options(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.NotFound, result.FirstError.Type);
    }

    [Fact]
    public async Task Resolve_ForeignWinner_NoRate_ReturnsFailure()
    {
        var price = ActivePrice(new PriceScope("EUR", null, null, null), 10m);
        var (prices, rates) = Repos([price], rate: null);

        var result = await ResolvePriceHandler.Handle(
            new ResolvePriceQuery(Product, "USD", 1, null, null, null, null), prices, rates, Options(), CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorType.Failure, result.FirstError.Type);
    }

    [Fact]
    public async Task Resolve_ForeignWinner_WithRate_Converts()
    {
        var price = ActivePrice(new PriceScope("EUR", null, null, null), 10m);
        var rate = ExchangeRate.Create("EUR", "USD", 1.1m, null, null, "tenant-1");
        var (prices, rates) = Repos([price], rate);

        var result = await ResolvePriceHandler.Handle(
            new ResolvePriceQuery(Product, "USD", 1, null, null, null, null), prices, rates, Options(), CancellationToken.None);

        Assert.False(result.IsError);
        Assert.True(result.Value.Converted);
        Assert.Equal(11.00m, result.Value.UnitAmount);
        Assert.Equal(1.1m, result.Value.RateApplied);
    }
}
