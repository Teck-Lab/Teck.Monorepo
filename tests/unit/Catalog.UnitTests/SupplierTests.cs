using Catalog.Domain.Entities;
using Xunit;

namespace Catalog.UnitTests;

public sealed class SupplierTests
{
    [Fact]
    public void Create_WithValidValues_SetsPropertiesAndIsActive()
    {
        var supplier = Supplier.Create("tenant-1", "Acme", "sales@acme.test", "+1-555-0100");

        Assert.NotEqual(Guid.Empty, supplier.Id);
        Assert.Equal("tenant-1", supplier.TenantId);
        Assert.Equal("Acme", supplier.Name);
        Assert.Equal("sales@acme.test", supplier.ContactEmail);
        Assert.Equal("+1-555-0100", supplier.ContactPhone);
        Assert.True(supplier.IsActive);
    }

    [Fact]
    public void Create_WithBlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() => Supplier.Create("tenant-1", " "));
    }

    [Fact]
    public void Deactivate_SetsIsActiveFalse()
    {
        var supplier = Supplier.Create("tenant-1", "Acme");

        supplier.Deactivate();

        Assert.False(supplier.IsActive);
    }

    [Fact]
    public void Activate_AfterDeactivate_SetsIsActiveTrue()
    {
        var supplier = Supplier.Create("tenant-1", "Acme");
        supplier.Deactivate();

        supplier.Activate();

        Assert.True(supplier.IsActive);
    }
}
