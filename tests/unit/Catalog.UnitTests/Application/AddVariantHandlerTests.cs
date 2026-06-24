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
        using var db = CatalogTestContext.CreateInMemory("addvariant");
        db.Products.Add(product);
        await db.SaveChangesAsync();
        var bus = Substitute.For<IMessageBus>();
        var command = new AddVariantCommand(product.Id, "WIDGET-2", 12.50m, "USD",
            [new VariantAttributeInput("Size", "Large")]);

        var result = await AddVariantHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("WIDGET-2", result.Value.Sku);
        Assert.False(result.Value.IsDefault);
        Assert.Equal("Large", Assert.Single(result.Value.Attributes).Value);
        await bus.Received(1).PublishAsync(Arg.Any<VariantCreatedIntegrationEvent>());
    }

    [Fact]
    public async Task Handle_WithMissingProduct_ReturnsNotFound()
    {
        using var db = CatalogTestContext.CreateInMemory("addvariant-missing");
        var bus = Substitute.For<IMessageBus>();
        var command = new AddVariantCommand(Guid.NewGuid(), "X", 1m, "USD", []);

        var result = await AddVariantHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
