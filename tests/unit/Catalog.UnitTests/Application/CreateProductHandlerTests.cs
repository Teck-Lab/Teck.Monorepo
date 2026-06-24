using Catalog.Application.Products.Features.CreateProduct.V1;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.UnitTests.TestContext;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Wolverine;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class CreateProductHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_PersistsDefaultVariantAndPublishesEvent()
    {
        using var db = CatalogTestContext.CreateInMemory();
        var bus = Substitute.For<IMessageBus>();
        var command = new CreateProductCommand("Widget", "A widget", null, "WIDGET-1", 9.99m, "USD");

        var dto = await CreateProductHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.Equal("Widget", dto.Name);
        Assert.True(dto.IsActive);
        var variant = Assert.Single(dto.Variants);
        Assert.True(variant.IsDefault);
        Assert.Equal(9.99m, variant.SellPriceAmount);
        Assert.Equal(1, await db.Products.CountAsync());
        await bus.Received(1).PublishAsync(Arg.Any<ProductCreatedIntegrationEvent>());
    }
}
