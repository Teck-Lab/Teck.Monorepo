using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Integration event published when a basket has been checked out. Consumed by the order service
/// to create an order.
/// </summary>
[MemoryPackable]
public partial class BasketCheckedOutIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="BasketCheckedOutIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public BasketCheckedOutIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the checked-out basket identifier.</summary>
    public Guid BasketId { get; set; }

    /// <summary>Gets or sets the owning customer identifier.</summary>
    public Guid? CustomerId { get; set; }

    /// <summary>Gets or sets the owning tenant identifier.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the basket subtotal.</summary>
    public decimal Subtotal { get; set; }

    /// <summary>Gets or sets the checkout timestamp.</summary>
    public DateTimeOffset CheckedOutAt { get; set; }

    /// <summary>Gets or sets the lines at checkout time.</summary>
    public List<BasketCheckedOutLine> Items { get; set; } = [];
}
