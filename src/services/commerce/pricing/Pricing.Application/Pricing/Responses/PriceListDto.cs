namespace Pricing.Application.Pricing.Responses;

/// <summary>A price list in API responses.</summary>
/// <param name="Id">The list identifier.</param>
/// <param name="Name">The display name.</param>
/// <param name="Description">The optional description.</param>
/// <param name="Status">The lifecycle status name.</param>
/// <param name="Currency">The scope currency.</param>
/// <param name="Country">The scope country, or null.</param>
/// <param name="CustomerGroupId">The scope customer group, or null.</param>
/// <param name="ChannelId">The scope channel, or null.</param>
/// <param name="ValidFrom">The inclusive validity start, or null.</param>
/// <param name="ValidUntil">The exclusive validity end, or null.</param>
/// <param name="Prices">The contained prices.</param>
public sealed record PriceListDto(
    Guid Id,
    string Name,
    string? Description,
    string Status,
    string Currency,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil,
    IReadOnlyList<PriceDto> Prices);
