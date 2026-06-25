using Catalog.Application.Suppliers.Mapping;
using Catalog.Domain.Entities;
using Catalog.Domain.ValueObjects;
using Xunit;

namespace Catalog.UnitTests.Application;

public sealed class SupplierMapperTests
{
    [Fact]
    public void ToDto_MapsSupplier()
    {
        var supplier = Supplier.Create("tenant-1", "Acme", "sales@acme.test", "+1-555-0100");

        var dto = supplier.ToDto();

        Assert.Equal(supplier.Id, dto.Id);
        Assert.Equal("Acme", dto.Name);
        Assert.Equal("sales@acme.test", dto.ContactEmail);
        Assert.True(dto.IsActive);
    }

    [Fact]
    public void ToDto_FlattensVariantSupplierCostPrice()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var supplierId = Guid.NewGuid();
        product.LinkSupplier(product.Variants[0].Id, supplierId, new Money(5m, "USD"), "ACME-9", 7, 10, isPreferred: true);
        var link = product.Variants[0].Suppliers[0];

        var dto = link.ToDto();

        Assert.Equal(supplierId, dto.SupplierId);
        Assert.Equal(5m, dto.CostPriceAmount);
        Assert.Equal("USD", dto.CostPriceCurrency);
        Assert.Equal("ACME-9", dto.SupplierSku);
        Assert.True(dto.IsPreferred);
    }

    [Fact]
    public void ToPriceHistory_MapsEachRow()
    {
        var product = Product.Create("tenant-1", "Widget", null, null, "WIDGET-1", new Money(9.99m, "USD"));
        var supplierId = Guid.NewGuid();
        product.LinkSupplier(product.Variants[0].Id, supplierId, new Money(5m, "USD"), "A", 7, 1, isPreferred: true);
        product.ChangeSupplierCost(product.Variants[0].Id, supplierId, new Money(6.50m, "USD"));
        var link = product.Variants[0].Suppliers[0];

        var history = link.PriceHistory.ToPriceHistory();

        Assert.Equal(2, history.Count);
        Assert.Contains(history, h => h.CostPriceAmount == 6.50m);
    }
}
