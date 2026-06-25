namespace Catalog.Application.Suppliers.Responses;

/// <summary>A variant↔supplier sourcing link with its flattened cost price.</summary>
public sealed record VariantSupplierDto(
    Guid Id,
    Guid SupplierId,
    decimal CostPriceAmount,
    string CostPriceCurrency,
    string SupplierSku,
    int LeadTimeDays,
    int MinOrderQuantity,
    bool IsPreferred);
