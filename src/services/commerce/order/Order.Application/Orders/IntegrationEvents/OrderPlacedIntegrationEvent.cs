using MemoryPack;
using Orders.Application.Orders.Responses;
using Orders.Domain.DomainEvents;
using Orders.Domain.Entities;
using SharedKernel.Core.Events;

namespace Orders.Application.Orders.IntegrationEvents;

/// <summary>
/// Integration event published to other services when an order has been placed.
/// </summary>
[MemoryPackable]
public partial class OrderPlacedIntegrationEvent : IntegrationEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="OrderPlacedIntegrationEvent"/> class.
    /// </summary>
    [MemoryPackConstructor]
    public OrderPlacedIntegrationEvent()
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderPlacedIntegrationEvent"/> class from a domain event.
    /// </summary>
    /// <param name="domainEvent">The domain event describing the placed order.</param>
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

    /// <summary>
    /// Initializes a new instance of the <see cref="OrderPlacedIntegrationEvent"/> class from an order entity.
    /// </summary>
    /// <param name="order">The order that was placed.</param>
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

    /// <summary>
    /// Gets or sets the identifier of the placed order.
    /// </summary>
    public Guid OrderId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the customer who placed the order.
    /// </summary>
    public Guid CustomerId { get; set; }

    /// <summary>
    /// Gets or sets the identifier of the tenant that owns the order.
    /// </summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the current status of the order.
    /// </summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>
    /// Gets or sets the total monetary value of the order.
    /// </summary>
    public decimal Total { get; set; }

    /// <summary>
    /// Gets or sets the timestamp at which the order was created.
    /// </summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>
    /// Gets or sets the line items that make up the order.
    /// </summary>
    public List<OrderLineDto> Lines { get; set; } = [];
}
