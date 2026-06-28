using Catalog.Application.Products.Features.UpdateSellPrice.V1;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using NSubstitute;
using Wolverine;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class UpdateSellPriceHandlerTests
{
    // See "Test strategy for load-then-mutate handlers": seed with a real context, act with a stubbed-save one.
    private static async Task<Product> SeedAsync(string name)
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        using var seed = CatalogTestContext.CreateInMemory(name);
        seed.Products.Add(product);
        await seed.SaveChangesAsync().ConfigureAwait(false);
        return product;
    }

    [Fact]
    public async Task Handle_WithNewPrice_UpdatesAndPublishes()
    {
        var product = await SeedAsync("price-change");
        using var db = CatalogTestContext.CreateWithStubbedSave("price-change");
        var repository = CatalogTestContext.WriteRepo<Product>(db);
        var unitOfWork = CatalogTestContext.UnitOfWork(db);
        var bus = Substitute.For<IMessageBus>();
        var command = new UpdateSellPriceCommand(product.Id, product.Variants[0].Id, 14.00m, "USD");

        var result = await UpdateSellPriceHandler.Handle(command, repository, unitOfWork, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(14.00m, result.Value.SellPriceAmount);
        await bus.Received(1).PublishAsync(Arg.Is<ProductPriceChangedIntegrationEvent>(e =>
            e.VariantId == product.Variants[0].Id &&
            e.OldAmount == 9.99m &&
            e.NewAmount == 14.00m &&
            e.Currency == "USD"));
    }

    [Fact]
    public async Task Handle_WithSamePrice_DoesNotPublish()
    {
        var product = await SeedAsync("price-same");
        using var db = CatalogTestContext.CreateWithStubbedSave("price-same");
        var repository = CatalogTestContext.WriteRepo<Product>(db);
        var unitOfWork = CatalogTestContext.UnitOfWork(db);
        var bus = Substitute.For<IMessageBus>();
        var command = new UpdateSellPriceCommand(product.Id, product.Variants[0].Id, 9.99m, "USD");

        var result = await UpdateSellPriceHandler.Handle(command, repository, unitOfWork, bus, CancellationToken.None);

        Assert.False(result.IsError);
        await bus.DidNotReceive().PublishAsync(Arg.Any<ProductPriceChangedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_WithMissingProduct_ReturnsNotFound()
    {
        using var db = CatalogTestContext.CreateWithStubbedSave("price-missing");
        var repository = CatalogTestContext.WriteRepo<Product>(db);
        var unitOfWork = CatalogTestContext.UnitOfWork(db);
        var bus = Substitute.For<IMessageBus>();
        var command = new UpdateSellPriceCommand(Guid.NewGuid(), Guid.NewGuid(), 1m, "USD");

        var result = await UpdateSellPriceHandler.Handle(command, repository, unitOfWork, bus, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
