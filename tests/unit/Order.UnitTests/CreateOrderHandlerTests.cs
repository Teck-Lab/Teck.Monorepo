using Finbuckle.MultiTenant.Abstractions;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using Orders.Application.Database;
using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;
using SharedKernel.Infrastructure.MultiTenant;
using Wolverine;
using Xunit;

namespace Orders.UnitTests;

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

        var tenantAccessor = Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();
        var db = Substitute.For<OrderDbContext>(options, tenantAccessor);
        db.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(1));

        var bus = Substitute.For<IMessageBus>();

        OrderDto result = await CreateOrderHandler.Handle(command, db, bus, CancellationToken.None);

        Assert.NotNull(result);
        Assert.Equal(command.CustomerId, result.CustomerId);
        Assert.Equal("Pending", result.Status);
    }
}
