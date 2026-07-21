using Customer.UnitTests.TestContext;
using Customers.Application.Customers.Features.AddCustomerAddress.V1;
using Xunit;

namespace Customer.UnitTests.Application;

public sealed class AddCustomerAddressHandlerTests
{
    private static async Task<Customers.Domain.Entities.Customer> SeedAsync(string name)
    {
        var customer = Customers.Domain.Entities.Customer.Create("tenant-1", "keycloak-sub-1", "jane.doe@example.com", "Jane", "Doe");
        using var seed = CustomerTestContext.CreateInMemory(name);
        seed.Customers.Add(customer);
        await seed.SaveChangesAsync().ConfigureAwait(false);
        return customer;
    }

    [Fact]
    public async Task Handle_WithExistingCustomer_AddsAddressAndReturnsDto()
    {
        var customer = await SeedAsync("address-found");
        using var db = CustomerTestContext.CreateWithStubbedSave("address-found");
        var repository = CustomerTestContext.WriteRepo<Customers.Domain.Entities.Customer>(db);
        var unitOfWork = CustomerTestContext.UnitOfWork(db);
        var command = new AddCustomerAddressCommand(customer.Id, "123 Main St", null, "Springfield", "12345", "US");

        var result = await AddCustomerAddressHandler.Handle(command, repository, unitOfWork, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("123 Main St", result.Value.Line1);
        Assert.Equal("Springfield", result.Value.City);
        Assert.Equal("12345", result.Value.PostalCode);
        Assert.Equal("US", result.Value.Country);
        Assert.True(result.Value.IsPrimary);
        Assert.NotEqual(Guid.Empty, result.Value.Id);
    }

    [Fact]
    public async Task Handle_WithMissingCustomer_ReturnsNotFound()
    {
        using var db = CustomerTestContext.CreateWithStubbedSave("address-missing");
        var repository = CustomerTestContext.WriteRepo<Customers.Domain.Entities.Customer>(db);
        var unitOfWork = CustomerTestContext.UnitOfWork(db);
        var command = new AddCustomerAddressCommand(Guid.NewGuid(), "123 Main St", null, "Springfield", "12345", "US");

        var result = await AddCustomerAddressHandler.Handle(command, repository, unitOfWork, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
