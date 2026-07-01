namespace Baskets.Application.Baskets.Responses;

/// <summary>Represents a basket together with its items in API responses.</summary>
/// <param name="Id">The basket identifier.</param>
/// <param name="CustomerId">The owning customer identifier, or null for a guest basket.</param>
/// <param name="AnonymousToken">The guest token, or null once owned by a customer.</param>
/// <param name="Status">The basket status name.</param>
/// <param name="Items">The basket items.</param>
/// <param name="Subtotal">The basket subtotal.</param>
public sealed record BasketDto(
    Guid Id,
    Guid? CustomerId,
    Guid? AnonymousToken,
    string Status,
    IReadOnlyList<BasketItemDto> Items,
    decimal Subtotal);
