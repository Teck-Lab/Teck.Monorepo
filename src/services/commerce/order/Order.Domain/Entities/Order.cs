using Orders.Domain.DomainEvents;
using Orders.Domain.Services;
using Orders.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Orders.Domain.Entities;

/// <summary>
/// Represents a customer order aggregate root.
/// </summary>
public sealed class Order : BaseEntity, IAggregateRoot, ITenantScoped
{
    private Order()
    {
        Lines = new List<OrderLine>();
    }

    /// <summary>
    /// Gets the identifier of the customer who owns the order.
    /// </summary>
    public Guid CustomerId { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets the current status of the order.
    /// </summary>
    public OrderStatus Status { get; private set; } = OrderStatus.Pending;

    /// <summary>
    /// Gets the lines that make up the order.
    /// </summary>
    public List<OrderLine> Lines { get; private set; }

    /// <summary>
    /// Gets the total monetary amount of the order.
    /// </summary>
    public decimal Total { get; private set; }

    /// <summary>
    /// Creates a new <see cref="Order"/> for the specified customer and order lines.
    /// </summary>
    /// <param name="customerId">The identifier of the customer who owns the order.</param>
    /// <param name="tenantId">The identifier of the tenant that owns the order.</param>
    /// <param name="lines">The lines that make up the order.</param>
    /// <returns>The newly created <see cref="Order"/>.</returns>
    public static Order Create(Guid customerId, string tenantId, List<OrderLine> lines)
    {
        ArgumentNullException.ThrowIfNull(lines);

        if (lines.Count == 0)
        {
            throw new ArgumentException("Order must contain at least one line.", nameof(lines));
        }

        Order order = new()
        {
            CustomerId = customerId,
            TenantId = tenantId,
            Status = OrderStatus.Pending,
            Lines = new List<OrderLine>(lines),
            Total = OrderPricingService.CalculateTotal(lines),
        };

        order.AddDomainEvent(new OrderPlaced(
            order.Id,
            order.CustomerId,
            order.TenantId,
            order.Status.Name,
            order.Total,
            new List<OrderLine>(order.Lines),
            DateTimeOffset.UtcNow));

        return order;
    }

    /// <summary>
    /// Recalculates the order total from its current lines.
    /// </summary>
    public void CalculateTotal()
    {
        Total = OrderPricingService.CalculateTotal(Lines);
    }
}
