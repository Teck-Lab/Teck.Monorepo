using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using ErrorOr;
using SharedKernel.Core.Database;

namespace Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;

/// <summary>Handles <see cref="LinkVariantSupplierCommand"/>.</summary>
public static class LinkVariantSupplierHandler
{
    /// <summary>Loads the owning product, links the supplier, and saves.</summary>
    /// <param name="command">The command describing the variant-supplier link to create.</param>
    /// <param name="repository">The write repository for loading and tracking the product.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The newly created variant-supplier DTO, or an error if the variant was not found.</returns>
    public static async Task<ErrorOr<VariantSupplierDto>> Handle(
        LinkVariantSupplierCommand command,
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

        var linkId = product.LinkSupplier(
            command.VariantId,
            command.SupplierId,
            new Money(command.CostAmount, command.CostCurrency),
            command.SupplierSku,
            command.LeadTimeDays,
            command.MinOrderQuantity,
            command.IsPreferred);

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        var link = product.Variants
            .Single(v => v.Id == command.VariantId).Suppliers
            .Single(s => s.Id == linkId);

        return link.ToDto();
    }
}
