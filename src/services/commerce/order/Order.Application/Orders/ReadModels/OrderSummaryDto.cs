namespace Orders.Application.Orders.ReadModels;

/// <summary>
/// Represents a condensed projection of an order used for list and summary views.
/// </summary>
public sealed record OrderSummaryDto(
    Guid Id,
    Guid CustomerId,
    string Status,
    decimal Total,
    DateTimeOffset CreatedAt);
