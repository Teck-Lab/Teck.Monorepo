using Ardalis.Specification;
using Customers.Application.Customers.Features.ListCustomers.V1;
using NSubstitute;
using SharedKernel.Core.Database;
using Xunit;

namespace Customer.UnitTests.Application;

public sealed class ListCustomersHandlerTests
{
    [Fact]
    public async Task Handle_ReturnsAllCustomersAsDtos()
    {
        var a = Customers.Domain.Entities.Customer.Create("tenant-1", "keycloak-sub-a", "a@example.com", "Alice", "Anderson");
        var b = Customers.Domain.Entities.Customer.Create("tenant-1", "keycloak-sub-b", "b@example.com", "Bob", "Brown");
        var repository = Substitute.For<IGenericReadRepository<Customers.Domain.Entities.Customer, Guid>>();
        repository.ListAsync(Arg.Any<ISpecification<Customers.Domain.Entities.Customer>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Customers.Domain.Entities.Customer>>([a, b]));

        var result = await ListCustomersHandler.Handle(new ListCustomersQuery(), repository, CancellationToken.None);

        Assert.Equal(2, result.Count);
        Assert.Contains(result, dto => dto.Id == a.Id);
        Assert.Contains(result, dto => dto.Id == b.Id);
    }

    [Fact]
    public async Task Handle_WhenNoCustomers_ReturnsEmptyList()
    {
        var repository = Substitute.For<IGenericReadRepository<Customers.Domain.Entities.Customer, Guid>>();
        repository.ListAsync(Arg.Any<ISpecification<Customers.Domain.Entities.Customer>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<IReadOnlyList<Customers.Domain.Entities.Customer>>([]));

        var result = await ListCustomersHandler.Handle(new ListCustomersQuery(), repository, CancellationToken.None);

        Assert.Empty(result);
    }
}
