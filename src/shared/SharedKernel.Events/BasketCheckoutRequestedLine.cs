using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Describes an unpriced basket line for asynchronous pricing.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BasketCheckoutRequestedLine
{
    /// <summary>Gets or sets the product identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid ProductId { get; set; }
    /// <summary>Gets or sets the requested quantity.</summary>
    [MemoryPackOrder(1)]
    public int Quantity { get; set; }
}
