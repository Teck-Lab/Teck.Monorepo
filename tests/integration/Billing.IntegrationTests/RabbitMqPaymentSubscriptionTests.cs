// <copyright file="RabbitMqPaymentSubscriptionTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Billings.Application.Billing.Payments;
using Billings.Application.Database;
using Billings.Domain.Entities;
using System.Collections;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Wolverine.RabbitMQ;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Proves the production Billing RabbitMQ subscription consumes V2 order events.</summary>
[Collection("SharedTestcontainers")]
public sealed class RabbitMqPaymentSubscriptionTests : BillingIntegrationTestBase
{
    private const string OrderPlacedV2HandlerQueue =
        "Billings.Application.Billing.EventHandlers.IntegrationEvents.OrderPlacedV2Handler";

    private const string OrderPlacedV2MessageQueue = "SharedKernel.Events.OrderPlacedV2IntegrationEvent";

    /// <summary>Initializes the broker-backed Billing host.</summary>
    public RabbitMqPaymentSubscriptionTests(SharedTestcontainersFixture fixture)
        : base(fixture, useRabbitMq: true)
    {
    }

    [Fact]
    public void OrderPlacedV2Listener_UsesDedicatedHandlerNamedQueue()
    {
        var queueNames = RabbitMqListenerQueueNames(WolverineHost);

        Assert.Contains(OrderPlacedV2HandlerQueue, queueNames);
        Assert.DoesNotContain(OrderPlacedV2MessageQueue, queueNames);
    }

    [Fact]
    public async Task PublishedOrderPlacedV2_PersistsExactlyOneTenantPaymentAndOutcome()
    {
        var orderId = Guid.NewGuid();
        const string tenantId = MockBearerAuthenticationHandler.TestTenantId;
        const string requestId = "rabbitmq-v2-payment-request";
        const string paymentMethodToken = "pm_opaque_broker_reference";
        var evt = new OrderPlacedV2IntegrationEvent
        {
            OrderId = orderId,
            BasketId = Guid.NewGuid(),
            CustomerId = Guid.NewGuid(),
            KeycloakSubjectId = "test-user",
            TenantId = tenantId,
            Amount = 42.50m,
            AuthorizedAmount = 50m,
            Currency = "USD",
            PaymentMethodToken = paymentMethodToken,
            RequestId = requestId,
            SourceCorrelationId = $"rabbitmq-{orderId:N}",
            CreatedAt = DateTimeOffset.UtcNow,
        };
        Provider.QueueAttemptResults(new PaymentProviderResult(true, "provider_opaque_reference", null) { Outcome = "succeeded" });

        using var publisher = await Host.CreateDefaultBuilder()
            .UseWolverine(options => options.UseRabbitMq(new Uri(RabbitMqConnectionString, UriKind.Absolute))
                .AutoProvision()
                .UseConventionalRouting())
            .StartAsync()
            .ConfigureAwait(false);
        await PublishAsync(publisher, evt).ConfigureAwait(false);
        await PublishAsync(publisher, evt).ConfigureAwait(false);

        var payment = await WaitForPaymentAsync(orderId).ConfigureAwait(false);

        Assert.Equal(tenantId, payment.TenantId);
        Assert.Equal(orderId, payment.OrderId);
        Assert.Equal(evt.Amount, payment.Amount.Amount);
        Assert.Equal(evt.Currency, payment.Amount.Currency);
        Assert.Equal(evt.AuthorizedAmount, payment.AuthorizedAmount.Amount);
        Assert.Equal(evt.Currency, payment.AuthorizedAmount.Currency);
        Assert.Equal(paymentMethodToken, payment.PaymentMethodToken);
        Assert.Equal(requestId, payment.RequestId);
        Assert.Equal("provider_opaque_reference", payment.ProviderReference);
        Assert.Single(payment.Attempts);
        Assert.Equal(1, Provider.AttemptCalls);
    }

    private static async Task PublishAsync(IHost publisher, OrderPlacedV2IntegrationEvent evt)
    {
        using var scope = publisher.Services.CreateScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>()
            .PublishAsync(evt, new DeliveryOptions { TenantId = evt.TenantId })
            .ConfigureAwait(false);
    }

    private async Task<Payment> WaitForPaymentAsync(Guid orderId)
    {
        for (var attempt = 0; attempt < 100; attempt++)
        {
            await using var scope = Services.CreateAsyncScope();
            var payment = await scope.ServiceProvider.GetRequiredService<BillingDbContext>().Payments
                .IgnoreQueryFilters()
                .Include(candidate => candidate.Attempts)
                .SingleOrDefaultAsync(candidate => candidate.OrderId == orderId)
                .ConfigureAwait(false);
            if (payment?.Status == PaymentStatus.Captured)
            {
                return payment;
            }

            await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
        }

        throw new Xunit.Sdk.XunitException("The RabbitMQ OrderPlacedV2 event did not produce a captured payment.");
    }

    private static string[] RabbitMqListenerQueueNames(IHost host)
    {
        var options = host.Services.GetRequiredService<WolverineOptions>();
        var transport = options.Transports.Cast<object>().Single(candidate =>
            candidate.GetType().FullName == "Wolverine.RabbitMQ.Internal.RabbitMqTransport");
        var queues = (IEnumerable?)transport.GetType().GetProperty("Queues")?.GetValue(transport)
            ?? throw new InvalidOperationException("The RabbitMQ transport did not expose its configured queues.");

        return queues.Cast<object>()
            .Select(candidate => candidate.GetType().GetProperty("Value")?.GetValue(candidate) ?? candidate)
            .Where(queue => queue.GetType().GetProperty("IsListener")?.GetValue(queue) is true)
            .Select(queue => queue.GetType().GetProperty("QueueName")?.GetValue(queue) as string)
            .OfType<string>()
            .ToArray();
    }
}
