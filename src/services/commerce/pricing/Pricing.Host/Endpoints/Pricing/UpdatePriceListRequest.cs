namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Request to update a price list's details, scope, and validity.</summary>
/// <param name="Id">The list identifier.</param>
/// <param name="Name">The new name.</param>
/// <param name="Description">The new description.</param>
/// <param name="Currency">The new scope currency.</param>
/// <param name="Country">The new scope country, or null.</param>
/// <param name="CustomerGroupId">The new scope customer group, or null.</param>
/// <param name="ChannelId">The new scope channel, or null.</param>
/// <param name="ValidFrom">The new inclusive validity start, or null.</param>
/// <param name="ValidUntil">The new exclusive validity end, or null.</param>
public sealed record UpdatePriceListRequest(
    Guid Id,
    string Name,
    string? Description,
    string Currency,
    string? Country,
    Guid? CustomerGroupId,
    Guid? ChannelId,
    DateTimeOffset? ValidFrom,
    DateTimeOffset? ValidUntil);
