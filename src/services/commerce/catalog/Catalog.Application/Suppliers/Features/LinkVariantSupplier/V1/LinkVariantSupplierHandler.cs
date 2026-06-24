using Ardalis.Specification.EntityFrameworkCore;
using Catalog.Application.Database;
using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;

/// <summary>Handles <see cref="LinkVariantSupplierCommand"/>.</summary>
public static class LinkVariantSupplierHandler
{
    /// <summary>Loads the owning product, links the supplier, and saves.</summary>
    public static async Task<ErrorOr<VariantSupplierDto>> Handle(
        LinkVariantSupplierCommand command,
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

        var linkId = product.LinkSupplier(
            command.VariantId,
            command.SupplierId,
            new Money(command.CostAmount, command.CostCurrency),
            command.SupplierSku,
            command.LeadTimeDays,
            command.MinOrderQuantity,
            command.IsPreferred);

        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        var link = product.Variants
            .Single(v => v.Id == command.VariantId).Suppliers
            .Single(s => s.Id == linkId);

        return link.ToDto();
    }
}
