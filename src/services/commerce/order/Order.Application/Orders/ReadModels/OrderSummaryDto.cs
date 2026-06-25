namespace Orders.Application.Orders.ReadModels;

public sealed record OrderSummaryDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal Total,
    DateTimeOffset CreatedAt);
