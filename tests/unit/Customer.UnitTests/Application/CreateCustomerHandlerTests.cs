using Customer.UnitTests.TestContext;
using Customers.Application.Customers;
using Customers.Application.Customers.Features.CreateCustomer.V1;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Customer.UnitTests.Application;

public sealed class CreateCustomerHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_PersistsPublishesAndReturnsDto()
    {
        using var db = CustomerTestContext.CreateInMemory();
        var repository = CustomerTestContext.WriteRepo<Customers.Domain.Entities.Customer>(db);
        var unitOfWork = CustomerTestContext.UnitOfWork(db);
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns("tenant-1");
        var identity = Substitute.For<ICustomerIdentityAccessor>();
        identity.KeycloakSubjectId.Returns("keycloak-sub-1");
        var bus = Substitute.For<IMessageBus>();
        var command = new CreateCustomerCommand("jane.doe@example.com", "Jane", "Doe");

        var dto = await CreateCustomerHandler.Handle(command, repository, unitOfWork, tenant, identity, bus, CancellationToken.None);

        Assert.Equal("jane.doe@example.com", dto.Email);
        Assert.Equal("Jane", dto.FirstName);
        Assert.Equal("Doe", dto.LastName);
        Assert.Equal("keycloak-sub-1", dto.KeycloakSubjectId);
        Assert.True(dto.IsActive);
        Assert.Equal(1, await db.Customers.CountAsync());

        await bus.Received(1).PublishAsync(Arg.Is<CustomerCreatedIntegrationEvent>(e =>
            e.CustomerId == dto.Id &&
            e.TenantId == "tenant-1" &&
            e.KeycloakSubjectId == "keycloak-sub-1" &&
            e.Email == "jane.doe@example.com"));
    }
}
