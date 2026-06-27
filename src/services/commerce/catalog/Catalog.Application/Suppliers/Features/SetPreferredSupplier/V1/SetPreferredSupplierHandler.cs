using Catalog.Application.Suppliers.ReadModels;
using Catalog.Domain.Entities;
using ErrorOr;
using SharedKernel.Core.Database;

namespace Catalog.Application.Suppliers.Features.SetPreferredSupplier.V1;

/// <summary>Handles <see cref="SetPreferredSupplierCommand"/>.</summary>
public static class SetPreferredSupplierHandler
{
    /// <summary>Enforces the single-preferred invariant via the domain and saves.</summary>
    /// <param name="command">The command identifying the variant and supplier to mark as preferred.</param>
    /// <param name="repository">The write repository for loading and tracking the product.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A success result, or an error if the variant or supplier link was not found.</returns>
    public static async Task<ErrorOr<Success>> Handle(
        SetPreferredSupplierCommand command,
        IGenericWriteRepository<Product, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        var product = await repository
            .FirstOrDefaultAsync(new ProductByVariantSpec(command.VariantId), enableTracking: true, ct)
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
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return Result.Success;
    }
}
