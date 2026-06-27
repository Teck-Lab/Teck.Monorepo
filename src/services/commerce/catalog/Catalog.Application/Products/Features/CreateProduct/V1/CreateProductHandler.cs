using Catalog.Application.Products.IntegrationEvents;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using SharedKernel.Core.Database;
using Wolverine;

namespace Catalog.Application.Products.Features.CreateProduct.V1;

/// <summary>Handles <see cref="CreateProductCommand"/>.</summary>
public static class CreateProductHandler
{
    /// <summary>Creates the product, persists it, and publishes <see cref="ProductCreatedIntegrationEvent"/>.</summary>
    /// <param name="command">The command describing the product to create.</param>
    /// <param name="repository">The write repository for persisting the product.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="bus">The message bus used to publish integration events.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<ProductDto> Handle(
        CreateProductCommand command,
        IGenericWriteRepository<Product, Guid> repository,
        IUnitOfWork unitOfWork,
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

        await repository.AddAsync(product, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new ProductCreatedIntegrationEvent(product)).ConfigureAwait(false);

        return product.ToDto();
    }
}
