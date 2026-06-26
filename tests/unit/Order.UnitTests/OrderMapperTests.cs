using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Mapping;
using Orders.Domain.Entities;
using Xunit;

namespace Orders.UnitTests;

public sealed class OrderMapperTests
{
    [Fact]
    public void ToDto_WithOrder_MapsCorrectly()
    {
        var customerId = Guid.NewGuid();
        var orderLine = new Orders.Domain.Entities.OrderLine(Guid.NewGuid(), "Test Product", 2, 10m);
        var order = Order.Create(customerId, "tenant-1", [orderLine]);

        var dto = OrderMapper.ToDto(order);

        Assert.Equal(order.Id, dto.Id);
        Assert.Equal(order.CustomerId, dto.CustomerId);
        Assert.Equal(order.Status.Name, dto.Status);
        Assert.Equal(order.Total, dto.Total);
        Assert.Equal(order.CreatedAt, dto.CreatedAt);
        Assert.Single(dto.Lines);
        Assert.Equal(orderLine.ProductId, dto.Lines[0].ProductId);
        Assert.Equal(orderLine.ProductName, dto.Lines[0].ProductName);
        Assert.Equal(orderLine.Quantity, dto.Lines[0].Quantity);
        Assert.Equal(orderLine.UnitPrice, dto.Lines[0].UnitPrice);
        Assert.Equal(orderLine.Total, dto.Lines[0].Total);
    }

    [Fact]
    public void ToEntity_WithCreateOrderCommand_MapsToTuple()
    {
        var customerId = Guid.NewGuid();
        var productId = Guid.NewGuid();
        var command = new CreateOrderCommand(
            customerId,
            [new CreateOrderLine(productId, "Test Product", 3, 15m)]);

        var entity = OrderMapper.ToEntity(command);

        Assert.Equal(customerId, entity.CustomerId);
        Assert.Equal(string.Empty, entity.TenantId);
        Assert.Single(entity.Lines);
        Assert.Equal(productId, entity.Lines[0].ProductId);
        Assert.Equal("Test Product", entity.Lines[0].ProductName);
        Assert.Equal(3, entity.Lines[0].Quantity);
        Assert.Equal(15m, entity.Lines[0].UnitPrice);
    }
}
