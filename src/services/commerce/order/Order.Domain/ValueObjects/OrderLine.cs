namespace Order.Domain.ValueObjects;

public sealed record OrderLine(Guid ProductId, string ProductName, int Quantity, decimal UnitPrice)
{
    public decimal Total => Quantity * UnitPrice;
}
