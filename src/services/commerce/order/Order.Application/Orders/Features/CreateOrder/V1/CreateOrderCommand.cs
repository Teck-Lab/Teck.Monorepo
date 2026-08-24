using Orders.Application.Orders.Responses;
using SharedKernel.Core.CQRS;

namespace Orders.Application.Orders.Features.CreateOrder.V1;

/// <summary>Creates an order only from the authoritative basket checkout event.</summary>
public sealed record CreateOrderCommand(
    Guid? CustomerId,
    string KeycloakSubjectId,
    Guid BasketId,
    string TenantId,
    decimal AuthorizedAmount,
    string Currency,
    string PaymentMethodToken,
    string SourceCorrelationId,
    List<CreateOrderLine> Lines) : ICommand<OrderDto>
{
    /// <summary>Initializes a legacy command shape for source-compatible in-process callers.</summary>
    /// <param name="customerId">The customer correlation.</param>
    /// <param name="lines">The caller lines.</param>
    public CreateOrderCommand(Guid customerId, List<CreateOrderLine> lines)
        : this(customerId, "legacy-subject", Guid.Empty, string.Empty, lines.Sum(line => line.Quantity * line.UnitPrice), "USD", string.Empty, Guid.NewGuid().ToString("N"), lines)
    {
    }
}
