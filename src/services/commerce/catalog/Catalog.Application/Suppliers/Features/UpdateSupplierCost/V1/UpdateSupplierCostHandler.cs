using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using SharedKernel.Core.Database;

namespace Catalog.Application.Suppliers.Features.UpdateSupplierCost.V1;

/// <summary>Handles <see cref="UpdateSupplierCostCommand"/>.</summary>
public static class UpdateSupplierCostHandler
{
    /// <summary>Changes the cost (appending history via the domain) and saves. Cost stays internal — no event published.</summary>
    /// <param name="command">The command describing the variant, supplier, and new cost.</param>
    /// <param name="repository">The write repository for loading and tracking the product.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The updated variant-supplier DTO, or an error if the variant or supplier link was not found.</returns>
    public static async Task<ErrorOr<VariantSupplierDto>> Handle(
        UpdateSupplierCostCommand command,
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
        var link = variant.Suppliers.FirstOrDefault(s => s.SupplierId == command.SupplierId);
        if (link is null)
        {
            return Error.NotFound(description: $"Supplier '{command.SupplierId}' is not linked to variant '{command.VariantId}'.");
        }

        product.ChangeSupplierCost(command.VariantId, command.SupplierId, new Money(command.CostAmount, command.CostCurrency));
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        return link.ToDto();
    }
}
