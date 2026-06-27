using Ardalis.Specification;
using ErrorOr;
using NSubstitute;
using Orders.Application.Orders.Features.GetOrder.V1;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using Xunit;

namespace Orders.UnitTests;

public sealed class GetOrderHandlerTests
{
    [Fact]
    public async Task Handle_WithValidOrderId_ReturnsOrderDto()
    {
        var customerId = Guid.NewGuid();
        var order = Order.Create(
            customerId,
            "tenant-1",
            [new Orders.Domain.Entities.OrderLine(Guid.NewGuid(), "Test Product", 1, 42m)]);
        var orderId = order.Id;

        var repository = Substitute.For<IGenericReadRepository<Order, System.Guid>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Order?>(order));

        var query = new GetOrderQuery(orderId);

        ErrorOr<OrderDto> result = await GetOrderHandler.Handle(query, repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(orderId, result.Value.Id);
    }
}
