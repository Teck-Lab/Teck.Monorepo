using Customer.UnitTests.TestContext;
using Customers.Application.Customers.Features.UpdateCustomerProfile.V1;
using Xunit;

namespace Customer.UnitTests.Application;

public sealed class UpdateCustomerProfileHandlerTests
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
    public async Task Handle_WithExistingCustomer_UpdatesNamesAndReturnsDto()
    {
        var customer = await SeedAsync("update-found");
        using var db = CustomerTestContext.CreateWithStubbedSave("update-found");
        var repository = CustomerTestContext.WriteRepo<Customers.Domain.Entities.Customer>(db);
        var unitOfWork = CustomerTestContext.UnitOfWork(db);
        var command = new UpdateCustomerProfileCommand(customer.Id, "Janet", "Smith");

        var result = await UpdateCustomerProfileHandler.Handle(command, repository, unitOfWork, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal("Janet", result.Value.FirstName);
        Assert.Equal("Smith", result.Value.LastName);
    }

    [Fact]
    public async Task Handle_WithMissingCustomer_ReturnsNotFound()
    {
        using var db = CustomerTestContext.CreateWithStubbedSave("update-missing");
        var repository = CustomerTestContext.WriteRepo<Customers.Domain.Entities.Customer>(db);
        var unitOfWork = CustomerTestContext.UnitOfWork(db);
        var command = new UpdateCustomerProfileCommand(Guid.NewGuid(), "Janet", "Smith");

        var result = await UpdateCustomerProfileHandler.Handle(command, repository, unitOfWork, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
