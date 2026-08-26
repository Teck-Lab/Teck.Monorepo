namespace Orders.Application.Orders.Responses;

/// <summary>
/// Represents an order together with its line items in API and event responses.
/// </summary>
public sealed record OrderDto(
    Guid Id,
    Guid? CustomerId,
    string Status,
    List<OrderLineDto> Lines,
    decimal Total,
    DateTimeOffset CreatedAt,
    string PaymentStatus,
    string StockStatus,
    decimal AuthorizedAmount,
    decimal CapturedAmount,
    string Currency,
    string FailureReason,
    string ActionText,
    bool RequiresHumanDecision);
