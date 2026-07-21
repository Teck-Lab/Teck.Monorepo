using MemoryPack;
using SharedKernel.Core.Events;

namespace SharedKernel.Events;

/// <summary>
/// Integration event published when a payment has failed. Consumed by the order service
/// to update the order payment status and record the failure reason.
/// </summary>
[MemoryPackable]
public partial class PaymentFailedIntegrationEvent : IntegrationEvent
{
    /// <summary>Initializes a new instance of the <see cref="PaymentFailedIntegrationEvent"/> class.</summary>
    [MemoryPackConstructor]
    public PaymentFailedIntegrationEvent()
    {
    }

    /// <summary>Gets or sets the payment identifier.</summary>
    public Guid PaymentId { get; set; }

    /// <summary>Gets or sets the order identifier.</summary>
    public Guid OrderId { get; set; }

    /// <summary>Gets or sets the owning tenant identifier.</summary>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets or sets the payment amount.</summary>
    public decimal Amount { get; set; }

    /// <summary>Gets or sets the currency code.</summary>
    public string Currency { get; set; } = string.Empty;

    /// <summary>Gets or sets the failure reason.</summary>
    public string Reason { get; set; } = string.Empty;
}
