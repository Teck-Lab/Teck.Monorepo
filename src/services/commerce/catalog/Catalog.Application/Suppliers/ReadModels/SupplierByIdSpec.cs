using Ardalis.Specification;
using Catalog.Domain.Entities;

namespace Catalog.Application.Suppliers.ReadModels;

/// <summary>Selects a single supplier by id.</summary>
public sealed class SupplierByIdSpec : Specification<Supplier>
{
    /// <summary>Initializes the spec.</summary>
    public SupplierByIdSpec(Guid supplierId) => Query.Where(s => s.Id == supplierId);
}
