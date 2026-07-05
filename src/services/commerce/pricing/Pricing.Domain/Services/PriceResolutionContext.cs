namespace Pricing.Domain.Services;

/// <summary>The input context for resolving a product's price.</summary>
/// <param name="Currency">The requested ISO currency (required).</param>
/// <param name="Quantity">The requested quantity (>= 1).</param>
/// <param name="Country">The optional request country.</param>
/// <param name="CustomerGroupId">The optional request customer group.</param>
/// <param name="ChannelId">The optional request channel.</param>
/// <param name="At">The moment at which to resolve.</param>
public sealed record PriceResolutionContext(
    string Currency,
    int Quantity,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset At);
