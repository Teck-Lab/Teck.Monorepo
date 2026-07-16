namespace Catalog.Host.Endpoints.Products;

/// <summary>Request to change a variant's sell price.</summary>
/// <param name="ProductId">The owning product identifier.</param>
/// <param name="VariantId">The variant identifier.</param>
/// <param name="Amount">The new sell price amount.</param>
/// <param name="Currency">The ISO currency code.</param>
public sealed record UpdateSellPriceRequest(Guid ProductId, Guid VariantId, decimal Amount, string Currency);
