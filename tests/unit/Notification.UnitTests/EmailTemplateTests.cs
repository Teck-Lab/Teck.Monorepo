using Notifications.Application.Notifications.EventHandlers.IntegrationEvents;
using Notifications.Application.Notifications.Features.QueueNotification.V1;
using NSubstitute;
using SharedKernel.Events;
using Wolverine;
using Xunit;

namespace Notifications.UnitTests;

public sealed class EmailTemplateTests
{
    [Fact]
    public async Task OrderEvents_RenderOnlyTheFiveFixedShopperSafeTemplates()
    {
        var paymentAction = await QueueAsync((bus, cancellationToken) => OrderPaymentActionRequiredHandler.Handle(new OrderPaymentActionRequiredIntegrationEvent { CustomerId = Guid.NewGuid(), OrderId = Guid.NewGuid(), KeycloakSubjectId = "subject", TenantId = "tenant", IdempotencyKey = "payment-action", SourceCorrelationId = "payment-action-correlation", ActionText = "fraudulent lost-card block-list" }, bus, cancellationToken));
        var confirmed = await QueueAsync((bus, cancellationToken) => OrderConfirmedHandler.Handle(new OrderConfirmedIntegrationEvent { CustomerId = Guid.NewGuid(), OrderId = Guid.NewGuid(), KeycloakSubjectId = "subject", TenantId = "tenant", IdempotencyKey = "confirmed", SourceCorrelationId = "confirmed-correlation", Amount = 12.34m, Currency = "USD" }, bus, cancellationToken));
        var cancelled = await QueueAsync((bus, cancellationToken) => OrderCancelledHandler.Handle(new OrderCancelledIntegrationEvent { CustomerId = Guid.NewGuid(), OrderId = Guid.NewGuid(), KeycloakSubjectId = "subject", TenantId = "tenant", IdempotencyKey = "cancelled", SourceCorrelationId = "cancelled-correlation", ActionText = "stolen-card explanation" }, bus, cancellationToken));
        var rejected = await QueueAsync((bus, cancellationToken) => OrderRejectedHandler.Handle(new OrderRejectedIntegrationEvent { CustomerId = Guid.NewGuid(), OrderId = Guid.NewGuid(), KeycloakSubjectId = "subject", TenantId = "tenant", IdempotencyKey = "rejected", SourceCorrelationId = "rejected-correlation", ActionText = "fraudulent payment block-list reason" }, bus, cancellationToken));
        var backorder = await QueueAsync((bus, cancellationToken) => OrderBackorderOutcomeHandler.Handle(new OrderBackorderOutcomeIntegrationEvent { CustomerId = Guid.NewGuid(), OrderId = Guid.NewGuid(), KeycloakSubjectId = "subject", TenantId = "tenant", IdempotencyKey = "backorder", SourceCorrelationId = "backorder-correlation", ActionText = "lost-card account warning" }, bus, cancellationToken));

        AssertTemplate(paymentAction, "Action needed for your order", "Please update your payment method to continue your order.");
        AssertTemplate(confirmed, "Your order is confirmed", $"Your order {confirmed.OrderId} is confirmed for 12.34 USD.");
        AssertTemplate(cancelled, "Your order was cancelled", "Your order was cancelled.");
        AssertTemplate(rejected, "Your order could not be completed", "Your order could not be completed. Please try another payment method.");
        AssertTemplate(backorder, "Update on your backordered order", "There is an update on your backordered order.");
    }

    [Fact]
    public async Task RejectedOrder_UsesOnlyGenericDeclineWording()
    {
        var bus = Substitute.For<IMessageBus>();
        QueueNotificationCommand? queued = null;
        bus.InvokeAsync(Arg.Any<QueueNotificationCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => { queued = callInfo.Arg<QueueNotificationCommand>(); return Task.FromResult<object?>(null); });
        var evt = new OrderRejectedIntegrationEvent { CustomerId = Guid.NewGuid(), OrderId = Guid.NewGuid(), KeycloakSubjectId = "subject", TenantId = "tenant", IdempotencyKey = "event", SourceCorrelationId = "correlation", ActionText = "fraudulent stolen-card block-list rationale" };

        await OrderRejectedHandler.Handle(evt, bus, CancellationToken.None);

        Assert.NotNull(queued);
        Assert.Equal("Your order could not be completed", queued!.Subject);
        Assert.DoesNotContain("fraudulent", queued.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lost card", queued.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stolen card", queued.Body, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SendHandler_ForwardsTenantAndStableDeliveryKeyToTheSender()
    {
        var sender = Substitute.For<Notifications.Application.Notifications.IEmailSender>();
        var message = new Notifications.Application.Notifications.EmailMessage("shopper@example.test", "Subject", "Body");

        await sender.SendAsync(message, "tenant-a", "delivery-key", CancellationToken.None);

        await sender.Received(1).SendAsync(message, "tenant-a", "delivery-key", CancellationToken.None);
    }

    private static async Task<QueueNotificationCommand> QueueAsync(Func<IMessageBus, CancellationToken, Task> invoke)
    {
        var bus = Substitute.For<IMessageBus>();
        QueueNotificationCommand? queued = null;
        bus.InvokeAsync(Arg.Any<QueueNotificationCommand>(), Arg.Any<CancellationToken>())
            .Returns(callInfo => { queued = callInfo.Arg<QueueNotificationCommand>(); return Task.FromResult<object?>(null); });

        await invoke(bus, CancellationToken.None).ConfigureAwait(false);

        return Assert.IsType<QueueNotificationCommand>(queued);
    }

    private static void AssertTemplate(QueueNotificationCommand command, string subject, string body)
    {
        Assert.Equal(subject, command.Subject);
        Assert.Equal(body, command.Body);
        Assert.DoesNotContain("fraudulent", command.Subject + command.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lost-card", command.Subject + command.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("lost card", command.Subject + command.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stolen-card", command.Subject + command.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("stolen card", command.Subject + command.Body, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("block-list", command.Subject + command.Body, StringComparison.OrdinalIgnoreCase);
    }
}
