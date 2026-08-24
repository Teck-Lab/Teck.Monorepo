using MemoryPack;

namespace SharedKernel.Events;

/// <summary>Describes a platform-priced basket line returned by pricing.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BasketPricedLine
{
    /// <summary>Gets or sets the product identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid ProductId { get; set; }
    /// <summary>Gets or sets the resolved unit price.</summary>
    [MemoryPackOrder(1)]
    public decimal UnitPrice { get; set; }
    /// <summary>Gets or sets the quantity.</summary>
    [MemoryPackOrder(2)]
    public int Quantity { get; set; }
    /// <summary>Gets or sets the resolved line total.</summary>
    [MemoryPackOrder(3)]
    public decimal LineTotal { get; set; }
}
