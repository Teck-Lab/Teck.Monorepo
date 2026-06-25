using Ardalis.Specification.EntityFrameworkCore;
using Catalog.Application.Database;
using Catalog.Application.Products.IntegrationEvents;
using Catalog.Application.Products.Mapping;
using Catalog.Application.Products.ReadModels;
using Catalog.Application.Products.Responses;
using Catalog.Domain.DomainEvents;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using Microsoft.EntityFrameworkCore;
using Wolverine;

namespace Catalog.Application.Products.Features.UpdateSellPrice.V1;

/// <summary>Handles <see cref="UpdateSellPriceCommand"/>.</summary>
public static class UpdateSellPriceHandler
{
    /// <summary>Changes the sell price; publishes <see cref="ProductPriceChangedIntegrationEvent"/> only on a real change.</summary>
    public static async Task<ErrorOr<VariantDto>> Handle(
        UpdateSellPriceCommand command,
        CatalogDbContext db,
        IMessageBus bus,
        CancellationToken ct)
    {
        var product = await db.Products
            .WithSpecification(new ProductByIdSpec(command.ProductId))
            .FirstOrDefaultAsync(ct)
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
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var priceChange = product.DomainEvents.OfType<VariantSellPriceChanged>().LastOrDefault();
        if (priceChange is not null)
        {
            await bus.PublishAsync(new ProductPriceChangedIntegrationEvent(priceChange, product.TenantId)).ConfigureAwait(false);
        }

        return variant.ToVariantDto();
    }
}
