using Catalog.Application.Products.IntegrationEvents;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.ReadModels;
using Catalog.Application.Products.Responses;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using SharedKernel.Core.Database;
using Wolverine;

namespace Catalog.Application.Products.Features.AddVariant.V1;

/// <summary>Handles <see cref="AddVariantCommand"/>.</summary>
public static class AddVariantHandler
{
    /// <summary>Loads the product, adds the variant, saves, and publishes <see cref="VariantCreatedIntegrationEvent"/>.</summary>
    /// <param name="command">The command describing the variant to add.</param>
    /// <param name="repository">The write repository for loading and tracking the product.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="bus">The message bus used to publish integration events.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The newly added variant DTO, or an error if the product was not found.</returns>
    public static async Task<ErrorOr<VariantDto>> Handle(
        AddVariantCommand command,
        IGenericWriteRepository<Product, Guid> repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var product = await repository
            .FirstOrDefaultAsync(new ProductByIdSpec(command.ProductId), enableTracking: true, ct)
            .ConfigureAwait(false);

        if (product is null)
        {
            return Error.NotFound(description: $"Product '{command.ProductId}' was not found.");
        }

        var attributes = command.Attributes.Select(a => new VariantAttribute(a.Name, a.Value)).ToList();
        var variantId = product.AddVariant(command.Sku, new Money(command.SellPriceAmount, command.SellPriceCurrency), attributes);

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new VariantCreatedIntegrationEvent(product.Id, variantId, command.Sku)).ConfigureAwait(false);

        return product.Variants.Single(v => v.Id == variantId).ToVariantDto();
    }
}
