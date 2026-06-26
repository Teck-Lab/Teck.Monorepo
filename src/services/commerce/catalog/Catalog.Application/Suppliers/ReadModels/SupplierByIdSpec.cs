using Ardalis.Specification;
using Catalog.Domain.Entities;

namespace Catalog.Application.Suppliers.ReadModels;

/// <summary>Selects a single supplier by id.</summary>
public sealed class SupplierByIdSpec : Specification<Supplier>
{
    /// <summary>Initializes the spec.</summary>
    /// <param name="supplierId">The identifier of the supplier to select.</param>
    public SupplierByIdSpec(Guid supplierId) => Query.Where(s => s.Id == supplierId);
}
