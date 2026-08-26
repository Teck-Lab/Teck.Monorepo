using NSubstitute;
using Orders.Application.Orders.EventHandlers.IntegrationEvents;
using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Responses;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Orders.UnitTests;

public sealed class BasketCheckedOutHandlerTests
{
    [Fact]
    public async Task Handle_V1CheckoutWithCustomer_IsIgnored()
    {
        var evt = new BasketCheckedOutIntegrationEvent
        {
            BasketId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            TenantId = "tenant-1",
            Subtotal = 20m,
            CheckedOutAt = DateTimeOffset.UtcNow,
            Items = [new BasketCheckedOutLine(Guid.NewGuid(), "Widget", 10m, 2, 20m)],
        };
        var bus = Substitute.For<IMessageBus>();

        await BasketCheckedOutHandler.Handle(evt, bus, CancellationToken.None);

        await bus.DidNotReceive().InvokeAsync<OrderDto>(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task Handle_V2Checkout_InvokesCreateOrderWithPlatformPricedEventLines()
    {
        var productId = Guid.NewGuid();
        var evt = new BasketCheckedOutV2IntegrationEvent
        {
            BasketId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            KeycloakSubjectId = "subject-1",
            TenantId = "tenant-1",
            Amount = 20m,
            AuthorizedAmount = 25m,
            Currency = "USD",
            PaymentMethodToken = "pm_token",
            SourceCorrelationId = "checkout-1",
            CheckedOutAt = DateTimeOffset.UtcNow,
            Items = [new BasketCheckedOutLineV2 { ProductId = productId, ProductName = "Widget", Quantity = 2, UnitPrice = 10m, LineTotal = 20m }],
        };
        var bus = Substitute.For<IMessageBus>();
        bus.InvokeAsync<OrderDto>(Arg.Any<CreateOrderCommand>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new OrderDto(Guid.NewGuid(), evt.CustomerId!.Value, "Pending", [], 20m, DateTimeOffset.UtcNow, "Pending", "Pending", 25m, 0m, "USD", "None", string.Empty, false)));

        await BasketCheckedOutV2Handler.Handle(evt, bus, CancellationToken.None);

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
}
