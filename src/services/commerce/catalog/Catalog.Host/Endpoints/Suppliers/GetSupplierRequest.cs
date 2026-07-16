namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to fetch a supplier by identifier.</summary>
/// <param name="SupplierId">The supplier identifier.</param>
public sealed record GetSupplierRequest(Guid SupplierId);
