using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using Xunit;

namespace Billing.UnitTests.Domain;

public sealed class InvoiceTests
{
    [Fact]
    public void Create_SetsLinesAndTotal()
    {
        var tenantId = "tenant-1";
        var orderId = Guid.NewGuid();
        var total = new Money(30.00m, "USD");
        var issuedAt = DateTimeOffset.UtcNow;
        var productId = Guid.NewGuid();
        var line = new InvoiceLineInput(productId, "Widget", 3, new Money(10.00m, "USD"));

        var invoice = Invoice.Create(tenantId, orderId, total, [line], issuedAt);

        Assert.Equal(tenantId, invoice.TenantId);
        Assert.Equal(orderId, invoice.OrderId);
        Assert.Equal(total, invoice.Amount);
        Assert.Equal(issuedAt, invoice.IssuedAt);

        InvoiceLine onlyLine = Assert.Single(invoice.Lines);
        Assert.Equal(productId, onlyLine.ProductId);
        Assert.Equal("Widget", onlyLine.Description);
        Assert.Equal(3, onlyLine.Quantity);
        Assert.Equal(new Money(10.00m, "USD"), onlyLine.UnitPrice);
    }

    [Fact]
    public void Create_NoLines_Throws()
    {
        var total = new Money(0m, "USD");

        Assert.Throws<ArgumentException>(() =>
            Invoice.Create("tenant-1", Guid.NewGuid(), total, [], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_EmptyOrderId_Throws()
    {
        var total = new Money(10.00m, "USD");
        var line = new InvoiceLineInput(Guid.NewGuid(), "Widget", 1, new Money(10.00m, "USD"));

        Assert.Throws<ArgumentException>(() =>
            Invoice.Create("tenant-1", Guid.Empty, total, [line], DateTimeOffset.UtcNow));
    }

    [Fact]
    public void Create_MultipleLines_PreservesOrder()
    {
        var total = new Money(50.00m, "USD");
        var productIdA = Guid.NewGuid();
        var productIdB = Guid.NewGuid();
        var lineA = new InvoiceLineInput(productIdA, "Widget A", 1, new Money(20.00m, "USD"));
        var lineB = new InvoiceLineInput(productIdB, "Widget B", 2, new Money(15.00m, "USD"));

        var invoice = Invoice.Create("tenant-1", Guid.NewGuid(), total, [lineA, lineB], DateTimeOffset.UtcNow);

        Assert.Equal(2, invoice.Lines.Count);
        Assert.Equal(productIdA, invoice.Lines[0].ProductId);
        Assert.Equal(productIdB, invoice.Lines[1].ProductId);
    }

    [Fact]
    public void Create_LineWithEmptyProductId_Throws()
    {
        var total = new Money(10.00m, "USD");
        var line = new InvoiceLineInput(Guid.Empty, "Widget", 1, new Money(10.00m, "USD"));

        Assert.Throws<ArgumentException>(() =>
            Invoice.Create("tenant-1", Guid.NewGuid(), total, [line], DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_LineWithBlankDescription_Throws(string description)
    {
        var total = new Money(10.00m, "USD");
        var line = new InvoiceLineInput(Guid.NewGuid(), description, 1, new Money(10.00m, "USD"));

        Assert.Throws<ArgumentException>(() =>
            Invoice.Create("tenant-1", Guid.NewGuid(), total, [line], DateTimeOffset.UtcNow));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public void Create_LineWithNonPositiveQuantity_Throws(int quantity)
    {
        var total = new Money(10.00m, "USD");
        var line = new InvoiceLineInput(Guid.NewGuid(), "Widget", quantity, new Money(10.00m, "USD"));

        Assert.Throws<ArgumentOutOfRangeException>(() =>
            Invoice.Create("tenant-1", Guid.NewGuid(), total, [line], DateTimeOffset.UtcNow));
    }
}
