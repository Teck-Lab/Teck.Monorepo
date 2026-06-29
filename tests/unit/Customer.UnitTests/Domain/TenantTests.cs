using Customers.Domain.Entities;
using Xunit;

namespace Customer.UnitTests.Domain;

public sealed class TenantTests
{
    [Fact]
    public void Create_SetsProvidedValues()
    {
        var id = Guid.NewGuid();
        var tenant = Tenant.Create(id, "acme", "shared", "postgres", hasReadReplicas: false);

        Assert.Equal(id, tenant.Id);
        Assert.Equal("acme", tenant.Identifier);
        Assert.Equal("shared", tenant.DatabaseStrategy);
        Assert.Equal("postgres", tenant.DatabaseProvider);
        Assert.False(tenant.HasReadReplicas);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsBlankIdentifier(string identifier)
    {
        Assert.Throws<ArgumentException>(() =>
            Tenant.Create(Guid.NewGuid(), identifier, "shared", "postgres", false));
    }
}
