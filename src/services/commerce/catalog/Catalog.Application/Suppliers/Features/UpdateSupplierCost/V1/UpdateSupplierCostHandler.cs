using Ardalis.Specification.EntityFrameworkCore;
using Catalog.Application.Database;
using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Application.Suppliers.Features.UpdateSupplierCost.V1;

/// <summary>Handles <see cref="UpdateSupplierCostCommand"/>.</summary>
public static class UpdateSupplierCostHandler
{
    /// <summary>Changes the cost (appending history via the domain) and saves. Cost stays internal — no event published.</summary>
    public static async Task<ErrorOr<VariantSupplierDto>> Handle(
        UpdateSupplierCostCommand command,
        CatalogDbContext db,
        CancellationToken ct)
    {
        var product = await db.Products
            .WithSpecification(new ProductByVariantSpec(command.VariantId))
            .FirstOrDefaultAsync(ct)
            .ConfigureAwait(false);

        if (product is null)
        {
            return Error.NotFound(description: $"Variant '{command.VariantId}' was not found.");
        }

        var variant = product.Variants.Single(v => v.Id == command.VariantId);
        var link = variant.Suppliers.FirstOrDefault(s => s.SupplierId == command.SupplierId);
        if (link is null)
        {
            return Error.NotFound(description: $"Supplier '{command.SupplierId}' is not linked to variant '{command.VariantId}'.");
        }

        product.ChangeSupplierCost(command.VariantId, command.SupplierId, new Money(command.CostAmount, command.CostCurrency));
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return variant.Suppliers.Single(s => s.SupplierId == command.SupplierId).ToDto();
    }
}
