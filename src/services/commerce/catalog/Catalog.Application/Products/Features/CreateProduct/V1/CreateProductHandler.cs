using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Catalog.Application.Products.Features.CreateProduct.V1;

/// <summary>Handles <see cref="CreateProductCommand"/>.</summary>
public static class CreateProductHandler
{
    /// <summary>Creates the product and publishes its default sell price for the pricing projection.</summary>
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

        var defaultVariant = product.Variants.Single(variant => variant.IsDefault);
        await bus.PublishAsync(new CatalogPriceChangedIntegrationEvent
        {
            ProductId = product.Id,
            VariantId = defaultVariant.Id,
            TenantId = product.TenantId,
            Amount = defaultVariant.SellPrice.Amount,
            Currency = defaultVariant.SellPrice.Currency,
            IdempotencyKey = $"catalog-price:{product.Id}:{defaultVariant.Id}:{product.CreatedAt.UtcTicks}",
            ChangedAt = product.CreatedAt,
        }).ConfigureAwait(false);

        return product.ToDto();
    }
}
