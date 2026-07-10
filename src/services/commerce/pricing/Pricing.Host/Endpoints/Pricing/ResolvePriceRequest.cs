namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Query parameters for resolving a product price.</summary>
/// <param name="ProductId">The product identifier.</param>
/// <param name="Currency">The requested ISO currency.</param>
/// <param name="Quantity">The requested quantity (defaults to 1).</param>
/// <param name="Country">The optional request country.</param>
/// <param name="CustomerGroupId">The optional request customer group.</param>
/// <param name="ChannelId">The optional request channel.</param>
/// <param name="At">The optional resolution moment.</param>
public sealed record ResolvePriceRequest(
    Guid ProductId,
    string Currency,
    int? Quantity,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? At);
