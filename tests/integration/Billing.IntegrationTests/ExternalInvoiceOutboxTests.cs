using Billings.Application.Billing.Purchases;
using Billings.Application.Database;
using Billings.Domain.Entities;
using MemoryPack;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;
using SharedKernel.Core.Pricing;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Xunit;

namespace Billing.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class ExternalInvoiceOutboxTests(SharedTestcontainersFixture fixture) : BillingIntegrationTestBase(fixture, useRabbitMq: true)
{
    [Fact]
    public async Task FailedInvoiceCommit_DoesNotPublishPaidEventAndRetryConfirmsSingleAuditedOrder()
    {
        _ = Client;
        var tenant = Guid.NewGuid();
        var order = TenantPurchaseOrder.Create(tenant, $"invoice-failure-{tenant:N}", TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "sales", "buyer");
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.Add(order);
            await db.SaveChangesAsync();
        }
        var rabbitFactory = new ConnectionFactory { Uri = new Uri(RabbitMqConnectionString, UriKind.Absolute) };
        await using var rabbit = await rabbitFactory.CreateConnectionAsync();
        await using var channel = await rabbit.CreateChannelAsync();
        const string exchange = "SharedKernel.Events.SubscriptionPaymentConfirmedIntegrationEvent";
        await channel.ExchangeDeclareAsync(exchange, ExchangeType.Fanout, durable: true);
        var queue = await channel.QueueDeclareAsync(queue: string.Empty, durable: false, exclusive: true, autoDelete: true);
        await channel.QueueBindAsync(queue.QueueName, exchange, string.Empty);
        string constraint = "test_invoice_fail_" + Guid.NewGuid().ToString("N");
        var confirmation = new ConfirmExternalInvoiceCommand(order.Id, "INV-CAPTURE-2026-001", "audited-manager");
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE tenant_purchase_orders ADD CONSTRAINT \"{constraint}\" " +
                $"CHECK (\"Id\" <> '{order.Id:D}'::uuid OR \"Status\" <> 'active')");
        }
        try
        {
            await using (var scope = Services.CreateAsyncScope())
            {
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                await Assert.ThrowsAnyAsync<DbUpdateException>(async () =>
                    await bus.InvokeAsync<TenantPurchaseOrderDto?>(confirmation));
            }
            await using (var scope = Services.CreateAsyncScope())
            {
                var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
                var stillPending = await db.TenantPurchaseOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
                Assert.Equal("pending", stillPending.Status);
                Assert.Equal(Guid.Empty, stillPending.SubscriptionId);
            }
            await Task.Delay(750);
            Assert.Null(await channel.BasicGetAsync(queue.QueueName, autoAck: true));
        }
        finally
        {
            await using var scope = Services.CreateAsyncScope();
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            await db.Database.ExecuteSqlRawAsync($"ALTER TABLE tenant_purchase_orders DROP CONSTRAINT \"{constraint}\"");
        }
        await using (var scope = Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
            var confirmed = await bus.InvokeAsync<TenantPurchaseOrderDto?>(confirmation);
            Assert.NotNull(confirmed);
            Assert.Equal("active", confirmed.Status);
            Assert.Equal("external_invoice", confirmed.PaymentMethod);
        }
        using var deadline = new CancellationTokenSource(TimeSpan.FromSeconds(15));
        BasicGetResult? delivered = null;
        while (!deadline.IsCancellationRequested && delivered is null)
        {
            delivered = await channel.BasicGetAsync(queue.QueueName, autoAck: true);
            if (delivered is null) await Task.Delay(100, deadline.Token);
        }
        Assert.NotNull(delivered);
        var paid = MemoryPackSerializer.Deserialize<SubscriptionPaymentConfirmedIntegrationEvent>(delivered.Body.Span);
        Assert.NotNull(paid);
        Assert.Equal(order.Id, paid.SourceOrderId);
        Assert.Equal(tenant, paid.TenantId);
        Assert.Equal("external_invoice", paid.PaymentMethod);
        using var key = System.Security.Cryptography.RSA.Create();
        key.ImportFromPem(BillingPaymentPublicKey);
        Assert.True(SubscriptionPaymentProof.Verify(paid, key));
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            var persisted = await db.TenantPurchaseOrders.AsNoTracking().SingleAsync(item => item.Id == order.Id);
            Assert.Equal(paid.SubscriptionId, persisted.SubscriptionId);
            Assert.Equal("INV-CAPTURE-2026-001", persisted.ExternalInvoiceReference);
        }
        await Task.Delay(500);
        Assert.Null(await channel.BasicGetAsync(queue.QueueName, autoAck: true));
    }
}
