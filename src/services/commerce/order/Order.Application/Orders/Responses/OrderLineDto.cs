namespace Orders.Application.Orders.Responses;

/// <summary>
/// Represents a single line item of an order in API and event responses.
/// </summary>
public sealed record OrderLineDto(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice,
    decimal Total);
