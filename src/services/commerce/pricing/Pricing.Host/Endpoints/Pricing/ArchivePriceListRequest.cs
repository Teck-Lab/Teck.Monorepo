namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Request to archive a price list.</summary>
/// <param name="Id">The list identifier.</param>
public sealed record ArchivePriceListRequest(Guid Id);
