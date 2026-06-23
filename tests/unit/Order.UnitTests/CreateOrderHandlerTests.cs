using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Order.Application.Orders.Features.CreateOrder.V1;
using Order.Application.Orders.Responses;
using Order.Domain.Entities;
using Order.Host.Database;
using Wolverine;
using Xunit;

namespace Order.UnitTests;

public sealed class CreateOrderHandlerTests
{
    [Fact]
    public async Task Handle_WithValidCommand_ReturnsOrderDto()
    {
        var command = new CreateOrderCommand(
            Guid.NewGuid(),
            [new CreateOrderLine(Guid.NewGuid(), "Test Product", 2, 12.50m)]);

        var options = new DbContextOptionsBuilder<OrderDbContext>()
            .UseInMemoryDatabase($"order-unit-tests-{Guid.NewGuid()}")
            .Options;

        var db = Substitute.For<OrderDbContext>(options);
        db.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));

        var bus = Substitute.For<IMessageBus>();

        OrderDto result = await CreateOrderHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(command.CustomerId, result.CustomerId);
        Assert.Equal("Pending", result.Status);
    }
}
