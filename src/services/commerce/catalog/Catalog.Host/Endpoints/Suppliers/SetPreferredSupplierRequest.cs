namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to set the preferred supplier for a variant.</summary>
/// <param name="VariantId">The variant identifier.</param>
/// <param name="SupplierId">The supplier identifier to mark preferred.</param>
public sealed record SetPreferredSupplierRequest(Guid VariantId, Guid SupplierId);
