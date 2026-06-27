using Catalog.Application.Suppliers.Mapping;
using Catalog.Application.Suppliers.ReadModels;
using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;
using ErrorOr;
using SharedKernel.Core.Database;

namespace Catalog.Application.Suppliers.Features.GetSupplier.V1;

/// <summary>Handles <see cref="GetSupplierQuery"/>.</summary>
public static class GetSupplierHandler
{
    /// <summary>Returns the supplier DTO or a NotFound error.</summary>
    /// <param name="query">The query identifying the supplier to return.</param>
    /// <param name="repository">The repository used to load the supplier.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task resolving to the supplier DTO or a NotFound error.</returns>
    public static async Task<ErrorOr<SupplierDto>> Handle(
        GetSupplierQuery query,
        IGenericReadRepository<Supplier, Guid> repository,
        CancellationToken ct)
    {
        var supplier = await repository.FirstOrDefaultAsync(new SupplierByIdSpec(query.SupplierId), ct).ConfigureAwait(false);

        return supplier is null
            ? Error.NotFound(description: $"Supplier '{query.SupplierId}' was not found.")
            : supplier.ToDto();
    }
}
