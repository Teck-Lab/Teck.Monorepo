namespace Catalog.Application.Suppliers.Responses;

/// <summary>An effective-dated supplier cost record.</summary>
public sealed record SupplierPriceHistoryDto(decimal CostPriceAmount, string CostPriceCurrency, DateTimeOffset EffectiveFrom);
