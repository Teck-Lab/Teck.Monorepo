using MemoryPack;

namespace SharedKernel.Events;

/// <summary>A platform-priced line carried by a version-two basket checkout event.</summary>
[MemoryPackable(GenerateType.VersionTolerant)]
public partial class BasketCheckedOutLineV2
{
    /// <summary>Gets or sets the product identifier.</summary>
    [MemoryPackOrder(0)]
    public Guid ProductId { get; set; }
    /// <summary>Gets or sets the product name captured at checkout.</summary>
    [MemoryPackOrder(1)]
    public string ProductName { get; set; } = string.Empty;
    /// <summary>Gets or sets the platform-resolved unit price.</summary>
    [MemoryPackOrder(2)]
    public decimal UnitPrice { get; set; }
    /// <summary>Gets or sets the quantity.</summary>
    [MemoryPackOrder(3)]
    public int Quantity { get; set; }
    /// <summary>Gets or sets the platform-resolved line total.</summary>
    [MemoryPackOrder(4)]
    public decimal LineTotal { get; set; }
}
