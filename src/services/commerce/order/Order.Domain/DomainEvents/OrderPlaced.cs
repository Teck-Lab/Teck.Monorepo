using Orders.Domain.ValueObjects;
using SharedKernel.Core.Events;

namespace Orders.Domain.DomainEvents;

/// <summary>
/// Domain event raised when an order has been placed.
/// </summary>
public sealed class OrderPlaced : DomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderPlaced"/> class.
    /// </summary>
    /// <param name="orderId">The identifier of the placed order.</param>
    /// <param name="customerId">The identifier of the customer who placed the order.</param>
    /// <param name="tenantId">The identifier of the tenant that owns the order.</param>
    /// <param name="status">The status of the order at the time it was placed.</param>
    /// <param name="total">The total monetary amount of the order.</param>
    /// <param name="lines">The lines that make up the order.</param>
    /// <param name="createdAt">The timestamp at which the order was created.</param>
    public OrderPlaced(Guid orderId, Guid customerId, string tenantId, string status, decimal total, List<OrderLine> lines, DateTimeOffset createdAt)
    {
        OrderId = orderId;
        CustomerId = customerId;
        TenantId = tenantId;
        Status = status;
        Total = total;
        Lines = lines;
        CreatedAt = createdAt;
    }

    /// <summary>
    /// Gets the identifier of the placed order.
    /// </summary>
    public Guid OrderId { get; }

    /// <summary>
    /// Gets the identifier of the customer who placed the order.
    /// </summary>
    public Guid CustomerId { get; }

    /// <summary>
    /// Gets the identifier of the tenant that owns the order.
    /// </summary>
    public string TenantId { get; }

    /// <summary>
    /// Gets the status of the order at the time it was placed.
    /// </summary>
    public string Status { get; }

    /// <summary>
    /// Gets the total monetary amount of the order.
    /// </summary>
    public decimal Total { get; }

    /// <summary>
    /// Gets the lines that make up the order.
    /// </summary>
    public List<OrderLine> Lines { get; }

    /// <summary>
    /// Gets the timestamp at which the order was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; }
}
