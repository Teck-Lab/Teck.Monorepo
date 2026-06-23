namespace Order.Domain.Entities;

public sealed class OrderLine
{
    public OrderLine(
        Guid productId,
        string productName,
        int quantity,
        decimal unitPrice)
    {
        ProductId = productId;
        ProductName = productName;
        Quantity = quantity;
        UnitPrice = unitPrice;
    }

    public Guid ProductId { get; }

    public string ProductName { get; }

    public int Quantity { get; }

    public decimal UnitPrice { get; }

    public decimal Total => Quantity * UnitPrice;
}
