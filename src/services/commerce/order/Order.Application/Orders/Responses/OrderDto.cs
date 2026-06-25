namespace Orders.Application.Orders.Responses;

public sealed record OrderDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    List<OrderLineDto> Lines,
    decimal Total,
    DateTimeOffset CreatedAt);
