using Orders.Domain.DomainEvents;
using Orders.Domain.Services;
using Orders.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Orders.Domain.Entities;

public sealed class Order : BaseEntity, IAggregateRoot, ITenantScoped
{
    private Order()
    {
        Lines = new List<OrderLine>();
    }

    public Guid CustomerId { get; private set; }

    public string TenantId { get; set; } = string.Empty;

    public OrderStatus Status { get; private set; } = OrderStatus.Pending;

    public List<OrderLine> Lines { get; private set; }

    public decimal Total { get; private set; }

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

    public void CalculateTotal()
    {
        Total = OrderPricingService.CalculateTotal(Lines);
    }
}
