using Ardalis.Specification;
using Customers.Application.Customers.Features.GetCustomer.V1;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Customer.UnitTests.Application;

public sealed class GetCustomerHandlerTests
{
    [Fact]
    public async Task Handle_WhenFound_ReturnsDto()
    {
        var customer = Customers.Domain.Entities.Customer.Create("tenant-1", "keycloak-sub-1", "jane.doe@example.com", "Jane", "Doe");
        var repository = Substitute.For<IGenericReadRepository<Customers.Domain.Entities.Customer, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Customers.Domain.Entities.Customer>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Customers.Domain.Entities.Customer?>(customer));

        var result = await GetCustomerHandler.Handle(new GetCustomerQuery(customer.Id), repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(customer.Id, result.Value.Id);
        Assert.Equal("jane.doe@example.com", result.Value.Email);
    }

    [Fact]
    public async Task Handle_WhenMissing_ReturnsNotFound()
    {
        var repository = Substitute.For<IGenericReadRepository<Customers.Domain.Entities.Customer, Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Customers.Domain.Entities.Customer>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Customers.Domain.Entities.Customer?>(null));

        var result = await GetCustomerHandler.Handle(new GetCustomerQuery(Guid.NewGuid()), repository, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(ErrorOr.ErrorType.NotFound, result.FirstError.Type);
    }
}
