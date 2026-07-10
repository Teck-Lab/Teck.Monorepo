namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Request to activate a price list.</summary>
/// <param name="Id">The list identifier.</param>
public sealed record ActivatePriceListRequest(Guid Id);
