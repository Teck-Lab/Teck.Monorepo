using Ardalis.Specification;
using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;
using ErrorOr;

namespace Catalog.Application.Suppliers.Features.GetSupplier.V1;

/// <summary>Handles <see cref="GetSupplierQuery"/>.</summary>
public static class GetSupplierHandler
{
    /// <summary>Returns the supplier DTO or a NotFound error.</summary>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task<ErrorOr<SupplierDto>> Handle(
        GetSupplierQuery query,
        IRepositoryBase<Supplier> repository,
        CancellationToken ct)
    {
        var supplier = await repository.FirstOrDefaultAsync(new SupplierByIdSpec(query.SupplierId), ct).ConfigureAwait(false);

        return supplier is null
            ? Error.NotFound(description: $"Supplier '{query.SupplierId}' was not found.")
            : supplier.ToDto();
    }
}
