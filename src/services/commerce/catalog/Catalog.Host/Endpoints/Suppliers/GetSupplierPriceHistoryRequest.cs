namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to fetch a variant-supplier cost history.</summary>
/// <param name="VariantId">The variant identifier.</param>
/// <param name="SupplierId">The supplier identifier.</param>
public sealed record GetSupplierPriceHistoryRequest(Guid VariantId, Guid SupplierId);
