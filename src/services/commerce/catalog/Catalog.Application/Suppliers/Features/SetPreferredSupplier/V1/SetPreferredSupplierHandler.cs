using Ardalis.Specification.EntityFrameworkCore;
using Catalog.Application.Database;
using Catalog.Application.Suppliers.ReadModels;
using ErrorOr;
using Microsoft.EntityFrameworkCore;

namespace Catalog.Application.Suppliers.Features.SetPreferredSupplier.V1;

/// <summary>Handles <see cref="SetPreferredSupplierCommand"/>.</summary>
public static class SetPreferredSupplierHandler
{
    /// <summary>Enforces the single-preferred invariant via the domain and saves.</summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<ErrorOr<Success>> Handle(
        SetPreferredSupplierCommand command,
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
        if (variant.Suppliers.All(s => s.SupplierId != command.SupplierId))
        {
            return Error.NotFound(description: $"Supplier '{command.SupplierId}' is not linked to variant '{command.VariantId}'.");
        }

        product.SetPreferredSupplier(command.VariantId, command.SupplierId);
        await db.SaveChangesAsync(ct).ConfigureAwait(false);

        return Result.Success;
    }
}
