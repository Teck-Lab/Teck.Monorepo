namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Request to create a price list.</summary>
/// <param name="Name">The display name.</param>
/// <param name="Description">The optional description.</param>
/// <param name="Currency">The scope ISO currency.</param>
/// <param name="Country">The scope country, or null.</param>
/// <param name="CustomerGroupId">The scope customer group, or null.</param>
/// <param name="ChannelId">The scope channel, or null.</param>
/// <param name="ValidFrom">The inclusive validity start, or null.</param>
/// <param name="ValidUntil">The exclusive validity end, or null.</param>
public sealed record CreatePriceListRequest(
    string Name,
    string? Description,
    string Currency,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil);
