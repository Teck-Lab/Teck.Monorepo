namespace Orders.Application.Orders.Features.CreateOrder.V1;

/// <summary>
/// Represents a single line item supplied when creating an order.
/// </summary>
public sealed record CreateOrderLine(
    Guid ProductId,
    string ProductName,
    int Quantity,
    decimal UnitPrice);
