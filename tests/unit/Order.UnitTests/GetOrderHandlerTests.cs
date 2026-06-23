using Ardalis.Specification;
using ErrorOr;
using NSubstitute;
using Order.Application.Orders.Features.GetOrder.V1;
using Order.Application.Orders.Responses;
using Order.Domain.Entities;
using Xunit;

namespace Order.UnitTests;

public sealed class GetOrderHandlerTests
{
    [Fact]
    public async Task Handle_WithValidOrderId_ReturnsOrderDto()
    {
        var customerId = Guid.NewGuid();
        var order = Order.Create(
            customerId,
            "tenant-1",
            [new Order.Domain.Entities.OrderLine(Guid.NewGuid(), "Test Product", 1, 42m)]);
        var orderId = order.Id;

        var repository = Substitute.For<IRepositoryBase<Order>>();
        repository.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Order?>(order));

        var query = new GetOrderQuery(orderId);

        ErrorOr<OrderDto> result = await GetOrderHandler.Handle(query, repository, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(orderId, result.Value.Id);
    }
}
