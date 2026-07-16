using Customers.Domain.DomainEvents;
using Customers.Domain.Entities;
using Xunit;

namespace Customer.UnitTests.Domain;

public sealed class CustomerTests
{
    private const string TenantId = "acme";
    private const string KeycloakSubjectId = "keycloak-sub-1";
    private const string Email = "jane.doe@example.com";
    private const string FirstName = "Jane";
    private const string LastName = "Doe";

    [Fact]
    public void Create_SetsProvidedValuesAndRaisesCustomerCreated()
    {
        var customer = Customers.Domain.Entities.Customer.Create(
            TenantId,
            KeycloakSubjectId,
            Email,
            FirstName,
            LastName);

        Assert.Equal(TenantId, customer.TenantId);
        Assert.Equal(KeycloakSubjectId, customer.KeycloakSubjectId);
        Assert.Equal(Email, customer.Email);
        Assert.Equal(FirstName, customer.FirstName);
        Assert.Equal(LastName, customer.LastName);
        Assert.True(customer.IsActive);
        Assert.Empty(customer.Addresses);

        var domainEvent = Assert.Single(customer.DomainEvents);
        var customerCreated = Assert.IsType<CustomerCreated>(domainEvent);
        Assert.Equal(customer.Id, customerCreated.CustomerId);
        Assert.Equal(TenantId, customerCreated.TenantId);
        Assert.Equal(KeycloakSubjectId, customerCreated.KeycloakSubjectId);
        Assert.Equal(Email, customerCreated.Email);
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsBlankEmail(string email)
    {
        Assert.Throws<ArgumentException>(() =>
            Customers.Domain.Entities.Customer.Create(TenantId, KeycloakSubjectId, email, FirstName, LastName));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Create_RejectsBlankKeycloakSubjectId(string keycloakSubjectId)
    {
        Assert.Throws<ArgumentException>(() =>
            Customers.Domain.Entities.Customer.Create(TenantId, keycloakSubjectId, Email, FirstName, LastName));
    }

    [Fact]
    public void UpdateProfile_UpdatesNames()
    {
        var customer = Customers.Domain.Entities.Customer.Create(TenantId, KeycloakSubjectId, Email, FirstName, LastName);

        customer.UpdateProfile("Janet", "Smith");

        Assert.Equal("Janet", customer.FirstName);
        Assert.Equal("Smith", customer.LastName);
    }

    [Fact]
    public void AddAddress_FirstAddressIsMarkedPrimaryAndReturnsNonEmptyId()
    {
        var customer = Customers.Domain.Entities.Customer.Create(TenantId, KeycloakSubjectId, Email, FirstName, LastName);

        var addressId = customer.AddAddress("123 Main St", null, "Springfield", "12345", "US");

        Assert.NotEqual(Guid.Empty, addressId);
        var address = Assert.Single(customer.Addresses);
        Assert.Equal(addressId, address.Id);
        Assert.True(address.IsPrimary);
        Assert.Equal("123 Main St", address.Line1);
        Assert.Null(address.Line2);
        Assert.Equal("Springfield", address.City);
        Assert.Equal("12345", address.PostalCode);
        Assert.Equal("US", address.Country);
    }

    [Fact]
    public void AddAddress_SecondAddressIsNotPrimary()
    {
        var customer = Customers.Domain.Entities.Customer.Create(TenantId, KeycloakSubjectId, Email, FirstName, LastName);
        customer.AddAddress("123 Main St", null, "Springfield", "12345", "US");

        customer.AddAddress("456 Oak Ave", "Apt 2", "Shelbyville", "54321", "US");

        Assert.Equal(2, customer.Addresses.Count);
        Assert.True(customer.Addresses[0].IsPrimary);
        Assert.False(customer.Addresses[1].IsPrimary);
    }
}
