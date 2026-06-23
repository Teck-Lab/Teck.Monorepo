using Catalog.Domain.Entities;
using Xunit;

namespace Catalog.UnitTests;

public sealed class CategoryTests
{
    [Fact]
    public void Create_WithValidValues_SetsProperties()
    {
        var category = Category.Create("tenant-1", "Beverages", "beverages");

        Assert.NotEqual(Guid.Empty, category.Id);
        Assert.Equal("tenant-1", category.TenantId);
        Assert.Equal("Beverages", category.Name);
        Assert.Equal("beverages", category.Slug);
        Assert.Null(category.ParentId);
    }

    [Fact]
    public void Create_WithParent_SetsParentId()
    {
        var parentId = Guid.NewGuid();

        var category = Category.Create("tenant-1", "Soda", "soda", parentId);

        Assert.Equal(parentId, category.ParentId);
    }

    [Fact]
    public void Create_WithBlankName_Throws()
    {
        Assert.Throws<ArgumentException>(() => Category.Create("tenant-1", " ", "slug"));
    }

    [Fact]
    public void Rename_WithValidName_UpdatesName()
    {
        var category = Category.Create("tenant-1", "Beverages", "beverages");

        category.Rename("Drinks");

        Assert.Equal("Drinks", category.Name);
    }
}
