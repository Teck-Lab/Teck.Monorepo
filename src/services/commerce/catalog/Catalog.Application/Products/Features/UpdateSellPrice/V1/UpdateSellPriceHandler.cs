using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.ReadModels;
using Catalog.Application.Products.Responses;
using Catalog.Domain.DomainEvents;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Catalog.Application.Products.Features.UpdateSellPrice.V1;

/// <summary>Handles <see cref="UpdateSellPriceCommand"/>.</summary>
public static class UpdateSellPriceHandler
{
    /// <summary>Changes the sell price and publishes a default-variant projection update on a real change.</summary>
    /// <param name="command">The command describing the variant and new sell price.</param>
    /// <param name="repository">The write repository for loading and tracking the product.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="bus">The message bus used to publish integration events.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated variant DTO, or an error if the product or variant was not found.</returns>
    public static async Task<ErrorOr<VariantDto>> Handle(
        UpdateSellPriceCommand command,
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

        var variant = product.Variants.FirstOrDefault(v => v.Id == command.VariantId);
        if (variant is null)
        {
            return Error.NotFound(description: $"Variant '{command.VariantId}' was not found.");
        }

        product.ChangeVariantSellPrice(command.VariantId, new Money(command.Amount, command.Currency));
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        var priceChange = product.DomainEvents.OfType<VariantSellPriceChanged>().LastOrDefault();
        if (priceChange is not null && variant.IsDefault)
        {
            await bus.PublishAsync(new CatalogPriceChangedIntegrationEvent
            {
                ProductId = priceChange.ProductId,
                VariantId = priceChange.VariantId,
                TenantId = product.TenantId,
                Amount = priceChange.NewAmount,
                Currency = priceChange.Currency,
                IdempotencyKey = $"catalog-price:{priceChange.ProductId}:{priceChange.VariantId}:{product.UpdatedOn?.UtcTicks ?? product.CreatedAt.UtcTicks}",
                ChangedAt = product.UpdatedOn ?? product.CreatedAt,
            }).ConfigureAwait(false);
        }

        return variant.ToVariantDto();
    }
}
