using Catalog.Application.Products.Features.AddVariant.V1;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Catalog.UnitTests.TestContext;
using NSubstitute;
using Wolverine;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class AddVariantHandlerTests
{
    [Fact]
    public async Task Handle_WithExistingProduct_AddsVariantAndPublishesEvent()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        using (var seed = CatalogTestContext.CreateInMemory("addvariant"))
        {
            seed.Products.Add(product);
            await seed.SaveChangesAsync();
        }

        using var db = CatalogTestContext.CreateWithStubbedSave("addvariant");
        var repository = CatalogTestContext.WriteRepo<Product>(db);
        var unitOfWork = CatalogTestContext.UnitOfWork(db);
        var bus = Substitute.For<IMessageBus>();
        var command = new AddVariantCommand(product.Id, "WIDGET-2", 12.50m, "USD",
            [new VariantAttributeInput("Size", "Large")]);

        var result = await AddVariantHandler.Handle(command, repository, unitOfWork, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("WIDGET-2", result.Value.Sku);
        Assert.False(result.Value.IsDefault);
        Assert.Equal("Large", Assert.Single(result.Value.Attributes).Value);
        await bus.Received(1).PublishAsync(Arg.Is<VariantCreatedIntegrationEvent>(e =>
            e.ProductId == product.Id &&
            e.VariantId == result.Value.Id &&
            e.Sku == "WIDGET-2"));
    }

    [Fact]
    public async Task Handle_WithMissingProduct_ReturnsNotFound()
    {
        using var db = CatalogTestContext.CreateInMemory("addvariant-missing");
        var repository = CatalogTestContext.WriteRepo<Product>(db);
        var unitOfWork = CatalogTestContext.UnitOfWork(db);
        var bus = Substitute.For<IMessageBus>();
        var command = new AddVariantCommand(Guid.NewGuid(), "X", 1m, "USD", []);

        var result = await AddVariantHandler.Handle(command, repository, unitOfWork, bus, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
