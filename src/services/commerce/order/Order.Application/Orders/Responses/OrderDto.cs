namespace Orders.Application.Orders.Responses;

/// <summary>
/// Represents an order together with its line items in API and event responses.
/// </summary>
public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    List<OrderLineDto> Lines,
    decimal Total,
    DateTimeOffset CreatedAt);
