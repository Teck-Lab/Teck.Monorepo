using SharedKernel.Core.Events;
using Order.Domain.Entities;

namespace Order.Domain.DomainEvents;

public sealed class OrderPlaced : DomainEvent
{
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

    public Guid OrderId { get; }
    public Guid CustomerId { get; }
    public string TenantId { get; }
    public string Status { get; }
    public decimal Total { get; }
    public List<OrderLine> Lines { get; }
    public DateTimeOffset CreatedAt { get; }
}
