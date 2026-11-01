using System.Net;
using System.Security.Cryptography;
using System.Text.Json;
using Billings.Application.Database;
using Billings.Domain.Entities;
using MemoryPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using SharedKernel.Core.Pricing;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Checks owner-scoped purchase cancellation at HTTP, PostgreSQL and real RabbitMQ boundaries.</summary>
[Collection("SharedTestcontainers")]
public sealed class PurchaseCancellationTests(SharedTestcontainersFixture fixture)
    : BillingIntegrationTestBase(fixture, useRabbitMq: true)
{
    [Fact]
    public async Task Only_owner_of_paid_order_can_cancel_once_and_publish_signed_suspension()
    {
        var pending = CreateOrder();
        var paid = CreateOrder();
        paid.ConfirmCapturedPayment(Guid.NewGuid(), DateTimeOffset.UtcNow.AddMinutes(-5));
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.AddRange(pending, paid);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        var rabbitFactory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString, UriKind.Absolute) };
        await using var rabbit = await rabbitFactory.CreateConnectionAsync().ConfigureAwait(false);
        await using var channel = await rabbit.CreateChannelAsync().ConfigureAwait(false);
        const string exchange = "SharedKernel.Events.SubscriptionAccessSuspendedIntegrationEvent";
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true).ConfigureAwait(false);
        var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true).ConfigureAwait(false);
        await channel.QueueBindAsync(queue.QueueName, exchange, string.Empty).ConfigureAwait(false);

        using (var pendingResponse = await Client.PostAsync($"/purchases/{pending.Id}/cancel", content: null).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.Conflict, pendingResponse.StatusCode);
        }

        using (var intruder = new HttpRequestMessage(HttpMethod.Post, $"/purchases/{paid.Id}/cancel"))
        {
            intruder.Headers.Add("X-Test-Onboarding-Subject", "intruder");
            using var denied = await Client.SendAsync(intruder).ConfigureAwait(false);
            Assert.Equal(HttpStatusCode.NotFound, denied.StatusCode);
        }

        using var cancelRequest = new HttpRequestMessage(HttpMethod.Post, $"/purchases/{paid.Id}/cancel");
        cancelRequest.Headers.Add("X-Test-Onboarding-Subject", "test-user");
        using (var cancelled = await Client.SendAsync(cancelRequest).ConfigureAwait(false))
        {
            Assert.Equal(HttpStatusCode.OK, cancelled.StatusCode);
            using var json = JsonDocument.Parse(await cancelled.Content.ReadAsStringAsync().ConfigureAwait(false));
            Assert.Equal("cancelled", json.RootElement.GetProperty("status").GetString());
            Assert.NotEqual(JsonValueKind.Null, json.RootElement.GetProperty("cancelledAtUtc").ValueKind);
        }

        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        BasicGetResult? delivered = null;
        while (!deadline.IsCancellationRequested && delivered is null)
        {
            delivered = await channel.BasicGetAsync(queue.QueueName, autoAck: true).ConfigureAwait(false);
            if (delivered is null)
            {
                await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
            }
        }

        Assert.NotNull(delivered);
        var suspended = MemoryPackSerializer.Deserialize<SubscriptionAccessSuspendedIntegrationEvent>(delivered.Body.Span);
        Assert.NotNull(suspended);
        Assert.Equal("cancelled", suspended.Reason);
        Assert.Equal(paid.TenantId, suspended.TenantId);
        Assert.Equal(paid.Id, suspended.SourceOrderId);
        Assert.Equal(paid.SubscriptionId, suspended.SubscriptionId);
        using (var publicKey = RSA.Create())
        {
            publicKey.ImportFromPem(BillingPaymentPublicKey);
            Assert.True(SubscriptionSuspensionProof.Verify(suspended, publicKey));
        }

        using var duplicateRequest = new HttpRequestMessage(HttpMethod.Post, $"/purchases/{paid.Id}/cancel");
        duplicateRequest.Headers.Add("X-Test-Onboarding-Subject", "test-user");
        using var duplicate = await Client.SendAsync(duplicateRequest).ConfigureAwait(false);
        Assert.Equal(HttpStatusCode.OK, duplicate.StatusCode);
        Assert.Null(await channel.BasicGetAsync(queue.QueueName, autoAck: true).ConfigureAwait(false));
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var recorded = await db.TenantPurchaseOrders.SingleAsync(item => item.Id == paid.Id).ConfigureAwait(false);
            Assert.Equal("cancelled", recorded.Status);
            Assert.Equal(suspended.SuspendedAtUtc.ToUnixTimeMilliseconds(), recorded.CancelledAtUtc?.ToUnixTimeMilliseconds());
        }
    }

    [Fact]
    public async Task Expired_paid_subscription_suspends_once_and_publishes_signed_failed_renewal()
    {
        var active = CreateOrder();
        active.ConfirmCapturedPayment(Guid.NewGuid(), DateTimeOffset.UtcNow.AddYears(-1).AddDays(-1));
        var future = CreateOrder();
        future.ConfirmCapturedPayment(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-3));
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.AddRange(active, future);
            await db.SaveChangesAsync().ConfigureAwait(false);
        }

        var rabbitFactory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString, UriKind.Absolute) };
        await using var rabbit = await rabbitFactory.CreateConnectionAsync().ConfigureAwait(false);
        await using var channel = await rabbit.CreateChannelAsync().ConfigureAwait(false);
        const string exchange = "SharedKernel.Events.SubscriptionAccessSuspendedIntegrationEvent";
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true).ConfigureAwait(false);
        var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true).ConfigureAwait(false);
        await channel.QueueBindAsync(queue.QueueName, exchange, string.Empty).ConfigureAwait(false);

        await using (var scope = Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
            var suspendedCount = await bus.InvokeAsync<int>(new Billings.Application.Billing.Purchases.SuspendExpiredPurchasesCommand(DateTimeOffset.UtcNow));
            var duplicateSuspendedCount = await bus.InvokeAsync<int>(new Billings.Application.Billing.Purchases.SuspendExpiredPurchasesCommand(DateTimeOffset.UtcNow.AddMinutes(1)));
            Assert.Equal(1, suspendedCount);
            Assert.Equal(0, duplicateSuspendedCount);
        }

        BasicGetResult? delivered = null;
        for (var attempt = 0; attempt < 40 && delivered is null; attempt++)
        {
            delivered = await channel.BasicGetAsync(queue.QueueName, autoAck: true).ConfigureAwait(false);
            if (delivered is null) await Task.Delay(TimeSpan.FromMilliseconds(100)).ConfigureAwait(false);
        }

        Assert.NotNull(delivered);
        var suspended = MemoryPackSerializer.Deserialize<SubscriptionAccessSuspendedIntegrationEvent>(delivered.Body.Span);
        Assert.NotNull(suspended);
        Assert.Equal("renewal_failed", suspended.Reason);
        Assert.Equal(active.TenantId, suspended.TenantId);
        Assert.Equal(active.TenantIdentifier, suspended.TenantIdentifier);
        Assert.Equal(active.Id, suspended.SourceOrderId);
        Assert.Equal(active.SubscriptionId, suspended.SubscriptionId);
        Assert.Equal(active.ValidUntilUtc!.Value.ToUnixTimeMilliseconds(), suspended.SuspendedAtUtc.ToUnixTimeMilliseconds());
        using (var key = RSA.Create())
        {
            key.ImportFromPem(BillingPaymentPublicKey);
            Assert.True(SubscriptionSuspensionProof.Verify(suspended, key));
        }

        Assert.Null(await channel.BasicGetAsync(queue.QueueName, autoAck: true).ConfigureAwait(false));
        await using var verificationScope = Services.CreateAsyncScope();
        var verificationDb = verificationScope.ServiceProvider.GetRequiredService<BillingDbContext>();
        Assert.Equal("suspended", (await verificationDb.TenantPurchaseOrders.SingleAsync(order => order.Id == active.Id)).Status);
        Assert.Equal("active", (await verificationDb.TenantPurchaseOrders.SingleAsync(order => order.Id == future.Id)).Status);
    }

    private static TenantPurchaseOrder CreateOrder()
    {
        var tenantId = Guid.NewGuid();
        return TenantPurchaseOrder.Create(tenantId, $"cancel-{tenantId:N}", TenantPlan.Business,
            "2026-10", TenantPlan.Business.DatabaseStrategy, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "test-user", "test-user");
    }
}
