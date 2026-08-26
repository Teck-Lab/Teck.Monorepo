using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Publishes a shopper-safe confirmed-order notification request.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class OrderConfirmedIntegrationEvent
{
    /// <summary>Gets or sets the order identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid OrderId { get; set; }
    /// <summary>Gets or sets the customer identifier.</summary>
    [MemoryPackOrder(1)]
    public Guid? CustomerId { get; set; }
    /// <summary>Gets or sets the immutable shopper subject.</summary>
    [MemoryPackOrder(2)]
    public string KeycloakSubjectId { get; set; } = string.Empty;
    /// <summary>Gets or sets the tenant identifier.</summary>
    [MemoryPackOrder(3)]
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets or sets the confirmed order amount.</summary>
    [MemoryPackOrder(4)]
    public decimal Amount { get; set; }
    /// <summary>Gets or sets the ISO currency code.</summary>
    [MemoryPackOrder(5)]
    public string Currency { get; set; } = string.Empty;
    /// <summary>Gets or sets the stable notification idempotency key.</summary>
    [MemoryPackOrder(6)]
    public string IdempotencyKey { get; set; } = string.Empty;
    /// <summary>Gets or sets the lifecycle source correlation identifier.</summary>
    [MemoryPackOrder(7)]
    public string SourceCorrelationId { get; set; } = string.Empty;
    /// <summary>Gets or sets the immutable shopper-authorized maximum total.</summary>
    [MemoryPackOrder(8)]
    public decimal AuthorizedAmount { get; set; }
}
