using Catalog.Application.Database;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Wolverine;

namespace Catalog.Application.Products.Features.CreateProduct.V1;

/// <summary>Handles <see cref="CreateProductCommand"/>.</summary>
public static class CreateProductHandler
{
    /// <summary>Creates the product, persists it, and publishes <see cref="ProductCreatedIntegrationEvent"/>.</summary>
    /// <param name="command">The command describing the product to create.</param>
    /// <param name="db">The catalog write context.</param>
    /// <param name="bus">The message bus used to publish integration events.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<ProductDto> Handle(
        CreateProductCommand command,
        CatalogDbContext db,
        IMessageBus bus,
        CancellationToken ct)
    {
        var product = Product.Create(
            string.Empty,
            command.Name,
            command.Description,
            command.CategoryId,
            command.Sku,
            new Money(command.SellPriceAmount, command.SellPriceCurrency));

        db.Products.Add(product);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new ProductCreatedIntegrationEvent(product)).ConfigureAwait(false);

        return product.ToDto();
    }
}
