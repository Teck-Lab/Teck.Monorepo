namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Request to remove a product's price from a list.</summary>
/// <param name="Id">The owning price list identifier.</param>
/// <param name="ProductId">The product identifier.</param>
public sealed record RemovePriceRequest(Guid Id, Guid ProductId);
