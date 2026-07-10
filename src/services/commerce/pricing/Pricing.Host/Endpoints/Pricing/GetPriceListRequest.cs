namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Request to retrieve a single price list by identifier.</summary>
/// <param name="Id">The price list identifier.</param>
public sealed record GetPriceListRequest(Guid Id);
