using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Integration event published when an order has been placed. Consumed by other services (e.g.
/// inventory to commit stock, billing to capture payment).
/// </summary>
[MemoryPackable]
public partial class OrderPlacedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="OrderPlacedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public OrderPlacedIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the identifier of the placed order.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Gets or sets the identifier of the customer who placed the order.</summary>
    public Guid CustomerId { get; set; }

    /// <summary>Gets or sets the identifier of the tenant that owns the order.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the current status of the order.</summary>
    public string Status { get; set; } = string.Empty;

    /// <summary>Gets or sets the total monetary value of the order.</summary>
    public decimal Total { get; set; }

    /// <summary>Gets or sets the timestamp at which the order was created.</summary>
    public DateTimeOffset CreatedAt { get; set; }

    /// <summary>Gets or sets the line items that make up the order.</summary>
    public List<OrderPlacedLine> Lines { get; set; } = [];
}
