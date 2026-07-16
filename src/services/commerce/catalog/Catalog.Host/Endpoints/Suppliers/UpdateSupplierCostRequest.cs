namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to change a variant-supplier cost price.</summary>
/// <param name="VariantId">The variant identifier.</param>
/// <param name="SupplierId">The supplier identifier.</param>
/// <param name="CostAmount">The new cost price amount.</param>
/// <param name="CostCurrency">The ISO currency code.</param>
public sealed record UpdateSupplierCostRequest(Guid VariantId, Guid SupplierId, decimal CostAmount, string CostCurrency);
