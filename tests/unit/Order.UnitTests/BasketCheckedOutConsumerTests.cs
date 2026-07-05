using NSubstitute;
using Orders.Application.Orders.EventHandlers.IntegrationEvents;
using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Responses;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Orders.UnitTests;

public sealed class BasketCheckedOutConsumerTests
{
    [Fact]
    public async Task Handle_InvokesCreateOrderWithMappedEventLines()
    {
        var productId = Guid.NewGuid();
        var evt = new BasketCheckedOutIntegrationEvent
        {
            BasketId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Subtotal = 20m,
            CheckedOutAt = DateTimeOffset.UtcNow,
            Items = [new BasketCheckedOutLine(productId, "Widget", 10m, 2, 20m)],
        };
        var bus = Substitute.For<IMessageBus>();
        bus.InvokeAsync<OrderDto>(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OrderDto(Guid.NewGuid(), evt.CustomerId!.Value, "Pending", [], 20m, DateTimeOffset.UtcNow)));

        await BasketCheckedOutConsumer.Handle(evt, bus, CancellationToken.None);

        // Asserts the per-line field mapping so a Quantity/UnitPrice swap fails the test.
        await bus.Received(1).InvokeAsync<OrderDto>(
            Arg.Is<CreateOrderCommand>(command =>
                command.CustomerId == evt.CustomerId
                && command.Lines.Count == 1
                && command.Lines[0].ProductId == productId
                && command.Lines[0].ProductName == "Widget"
                && command.Lines[0].Quantity == 2
                && command.Lines[0].UnitPrice == 10m),
            Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_GuestCheckoutWithoutCustomer_DoesNotCreateOrder()
    {
        var evt = new BasketCheckedOutIntegrationEvent
        {
            BasketId = Guid.NewGuid(),
            CustomerId = null,
            TenantId = "tenant-1",
            Subtotal = 20m,
            CheckedOutAt = DateTimeOffset.UtcNow,
            Items = [new BasketCheckedOutLine(Guid.NewGuid(), "Widget", 10m, 2, 20m)],
        };
        var bus = Substitute.For<IMessageBus>();

        await BasketCheckedOutConsumer.Handle(evt, bus, CancellationToken.None);

        await bus.DidNotReceive().InvokeAsync<OrderDto>(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>());
    }
}
