namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Request to link a supplier to a variant.</summary>
/// <param name="VariantId">The variant identifier.</param>
/// <param name="SupplierId">The supplier identifier.</param>
/// <param name="CostAmount">The cost price amount.</param>
/// <param name="CostCurrency">The ISO currency code.</param>
/// <param name="SupplierSku">The supplier's SKU for the variant.</param>
/// <param name="LeadTimeDays">The sourcing lead time in days.</param>
/// <param name="MinOrderQuantity">The minimum order quantity.</param>
/// <param name="IsPreferred">Whether this link is the preferred supplier.</param>
public sealed record LinkVariantSupplierRequest(
    Guid VariantId,
    Guid SupplierId,
    decimal CostAmount,
    string CostCurrency,
    string SupplierSku,
    int LeadTimeDays,
    int MinOrderQuantity,
    bool IsPreferred);
