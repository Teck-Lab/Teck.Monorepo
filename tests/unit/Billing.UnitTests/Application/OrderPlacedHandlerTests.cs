using Billings.Application.Billing;
using Billings.Application.Billing.EventHandlers.IntegrationEvents;
using Billings.Application.Billing.Payments.Features.CapturePayment.V1;
using Billings.Application.Billing.Payments.Responses;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Billing.UnitTests.Application;

public sealed class OrderPlacedHandlerTests
{
    private static IOptions<PaymentProviderOptions> Options() =>
        Microsoft.Extensions.Options.Options.Create(new PaymentProviderOptions { DefaultCurrency = "USD" });

    private static OrderPlacedIntegrationEvent OrderFor(Guid orderId, Guid customerId, decimal total) => new()
    {
        OrderId = orderId,
        CustomerId = customerId,
        TenantId = "tenant-1",
        Status = "Placed",
        Total = total,
        CreatedAt = DateTimeOffset.UtcNow,
        Lines = [],
    };

    [Fact]
    public async Task Handle_MapsEventToCapturePaymentCommandAndInvokes()
    {
        var orderId = Guid.NewGuid();
        var customerId = Guid.NewGuid();
        var evt = OrderFor(orderId, customerId, 42.50m);
        var bus = Substitute.For<IMessageBus>();
        bus.InvokeAsync<PaymentDto>(Arg.Any<object>(), Arg.Any<CancellationToken>())
            .Returns(Task.FromResult(new PaymentDto(Guid.NewGuid(), orderId, customerId, 42.50m, "USD", "Captured", null)));

        await OrderPlacedHandler.Handle(evt, bus, Options(), CancellationToken.None);

        await bus.Received(1).InvokeAsync<PaymentDto>(
            Arg.Is<CapturePaymentCommand>(c =>
                c.OrderId == evt.OrderId
                && c.CustomerId == evt.CustomerId
                && c.Amount == evt.Total
                && c.Currency == "USD"),
            CancellationToken.None);
    }

    [Fact]
    public async Task Handle_NullEvent_ThrowsArgumentNullException()
    {
        var bus = Substitute.For<IMessageBus>();

        await Assert.ThrowsAsync<ArgumentNullException>(
            () => OrderPlacedHandler.Handle(null!, bus, Options(), CancellationToken.None));
    }
}
