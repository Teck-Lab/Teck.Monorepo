using MemoryPack;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;
using Orders.Domain.DomainEvents;
using SharedKernel.Core.Events;

namespace Orders.Application.Orders.IntegrationEvents;

[MemoryPackable]
public partial class OrderPlacedIntegrationEvent : IntegrationEvent
{
    public Guid OrderId { get; set; }

    public Guid CustomerId { get; set; }

    public string TenantId { get; set; } = string.Empty;

    public string Status { get; set; } = string.Empty;

    public decimal Total { get; set; }

    public DateTimeOffset CreatedAt { get; set; }

    public List<OrderLineDto> Lines { get; set; } = [];

    [MemoryPackConstructor]
    public OrderPlacedIntegrationEvent()
    {
    }

    public OrderPlacedIntegrationEvent(OrderPlaced domainEvent)
    {
        OrderId = domainEvent.OrderId;
        CustomerId = domainEvent.CustomerId;
        TenantId = domainEvent.TenantId;
        Status = domainEvent.Status;
        Total = domainEvent.Total;
        CreatedAt = domainEvent.CreatedAt;
        Lines = domainEvent.Lines.Select(line => new OrderLineDto(
            line.ProductId,
            line.ProductName,
            line.Quantity,
            line.UnitPrice,
            line.Total)).ToList();
    }

    public OrderPlacedIntegrationEvent(Order order)
    {
        OrderId = order.Id;
        CustomerId = order.CustomerId;
        TenantId = order.TenantId;
        Status = order.Status.Name;
        Total = order.Total;
        CreatedAt = order.CreatedAt;
        Lines = order.Lines.Select(line => new OrderLineDto(
            line.ProductId,
            line.ProductName,
            line.Quantity,
            line.UnitPrice,
            line.Total)).ToList();
    }
}
