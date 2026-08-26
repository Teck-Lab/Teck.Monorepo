// <copyright file="CheckoutToPaidOrderLifecycleTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Finbuckle.MultiTenant.Abstractions;
using System.Net.Http.Json;
using NSubstitute;
using Orders.Application.Database;
using Orders.Application.Orders.EventHandlers.IntegrationEvents;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Wolverine.Tracking;
using Xunit;

namespace CrossService.IntegrationTests;

/// <summary>
/// Retains focused order-handler coverage while the production-host fact below is the acceptance
/// proof: it crosses the real basket, pricing, order, inventory, billing, customer, and
/// notification seams over shared PostgreSQL and RabbitMQ resources.
/// </summary>
[Collection("SharedTestcontainers")]
public sealed class CheckoutToPaidOrderLifecycleTests(SharedTestcontainersFixture fixture)
{
    private const string TenantId = DeterministicBearerToken.TenantId;

    /// <summary>
    /// Proves the supported basket HTTP ingress reaches confirmed order, reserved stock, and one
    /// durable sent notification without invoking a lifecycle application handler from test code.
    /// </summary>
    [Fact]
    public async Task PlatformPricedCheckout_TraversesProductionHostsAndPersistsOneSentNotification()
    {
        using var harness = new ProductionLifecycleHarness(fixture);
        await harness.SeedCustomerAsync();
        var contact = await harness.WaitForCustomerContactAsync();
        Assert.NotNull(contact);
        Assert.Equal("shopper@example.test", contact!.Email);
        var product = await harness.CreateProductAsync(10m);
        await harness.RegisterStockAsync(product.Id, quantity: 5);

        Guid basketId = await harness.CheckoutAsync(product.Id, authorizedAmount: 25m);
        Order? order = await harness.WaitForConfirmedOrderAsync(basketId);

        Assert.NotNull(order);
        Assert.True(order!.Status == OrderStatus.Confirmed,
            $"Expected confirmed order but observed status {order.Status}, payment {order.PaymentState}, and stock {order.StockState}.");
        Assert.Equal(PaymentState.Captured, order.PaymentState);
        Assert.Equal(20m, order.Total);
        Assert.Equal(20m, order.CapturedAmount);
        Assert.Equal(25m, order.AuthorizedAmount);
        Assert.True(await harness.WaitForReservedAvailabilityAsync(product.Id));

        var payment = await harness.WaitForCapturedPaymentAsync(order.Id);
        Assert.NotNull(payment);
        Assert.Equal(TenantId, payment!.TenantId);
        Assert.Equal(20m, payment.Amount.Amount);
        Assert.Equal(25m, payment.AuthorizedAmount.Amount);

        var delivery = await harness.WaitForSentNotificationAsync(order.Id);
        Assert.NotNull(delivery);
        Assert.Equal(TenantId, delivery!.TenantId);
        Assert.Equal("shopper@example.test", delivery!.Recipient);
        Assert.Equal("Your order is confirmed", delivery.Subject);
        Assert.Equal($"Your order {order.Id} is confirmed for 20.00 USD.", delivery.Body);

        var acceptance = await harness.WaitForStubAcceptanceAsync(delivery.IdempotencyKey);
        Assert.NotNull(acceptance);
        Assert.Equal(TenantId, acceptance!.TenantId);
        Assert.Equal(delivery.IdempotencyKey, acceptance.IdempotencyKey);
        Assert.Equal(delivery.Recipient, acceptance.Recipient);
        Assert.Equal(delivery.Subject, acceptance.Subject);
        Assert.Equal(delivery.Body, acceptance.Body);
    }

    // Gateway.Public.IntegrationTests.TokenTenantMismatch_Returns403 owns claim/header mismatch coverage.

    [Fact]
    public async Task CheckoutV2IngressIsPresentWhileRetiredCallerPricedRoutesAreAbsent()
    {
        using var harness = new ProductionLifecycleHarness(fixture);

        using var createOrder = await harness.Order.PostAsJsonAsync("/orders", new { Total = 0.01m, Currency = "USD" });
        using var capturePayment = await harness.Billing.PostAsJsonAsync("/payments", new { Amount = 0.01m, Currency = "USD" });

        Assert.False(createOrder.IsSuccessStatusCode);
        Assert.False(capturePayment.IsSuccessStatusCode);

        var product = await harness.CreateProductAsync(10m);
        await harness.RegisterStockAsync(product.Id, quantity: 5);
        Guid basketId = await harness.CheckoutAsync(product.Id, authorizedAmount: 25m);
        Assert.NotNull(await harness.WaitForConfirmedOrderAsync(basketId));
    }

    [Fact]
    public async Task BackorderedCheckout_ReplenishmentThroughInventoryHttpConvergesAtPlatformAmount()
    {
        using var harness = new ProductionLifecycleHarness(fixture);
        await harness.SeedCustomerAsync();
        Assert.NotNull(await harness.WaitForCustomerContactAsync());
        var product = await harness.CreateProductAsync(10m);
        var stock = await harness.RegisterStockAsync(product.Id, quantity: 0, allowBackorder: true);

        Guid basketId = await harness.CheckoutAsync(product.Id, authorizedAmount: 25m);
        var pending = await harness.WaitForOrderAsync(basketId, TimeSpan.FromSeconds(20));
        Assert.NotNull(pending);
        Assert.Equal(20m, pending!.Total);
        Assert.Equal(25m, pending.AuthorizedAmount);

        await harness.AdjustStockAsync(stock.Id, delta: 2);
        var confirmed = await harness.WaitForConfirmedOrderAsync(basketId);
        Assert.NotNull(confirmed);
        Assert.Equal(20m, confirmed!.Total);
        Assert.Equal(20m, confirmed.CapturedAmount);

        var payment = await harness.WaitForCapturedPaymentAsync(confirmed.Id);
        Assert.NotNull(payment);
        Assert.Equal(TenantId, payment!.TenantId);
        Assert.Equal(20m, payment.Amount.Amount);

        var delivery = await harness.WaitForSentNotificationAsync(confirmed.Id);
        Assert.NotNull(delivery);
        Assert.Equal(TenantId, delivery!.TenantId);
        Assert.Equal("Your order is confirmed", delivery.Subject);
        Assert.Equal(1, await harness.CountNotificationDeliveriesAsync(confirmed.Id));
    }

    [Fact]
    public async Task ForeignTenantSignedPrincipalCannotReadAnotherTenantsConfirmedOrder()
    {
        using var harness = new ProductionLifecycleHarness(fixture);
        await harness.SeedCustomerAsync();
        Assert.NotNull(await harness.WaitForCustomerContactAsync());
        var product = await harness.CreateProductAsync(10m);
        await harness.RegisterStockAsync(product.Id, quantity: 5);
        Guid basketId = await harness.CheckoutAsync(product.Id, authorizedAmount: 25m);
        var order = await harness.WaitForConfirmedOrderAsync(basketId);
        Assert.NotNull(order);
        Assert.NotNull(await harness.WaitForSentNotificationAsync(order!.Id));

        int ordersBeforeForeignRequest = await harness.CountOrdersAsync();
        int paymentsBeforeForeignRequest = await harness.CountPaymentsAsync();
        int deliveriesBeforeForeignRequest = await harness.CountNotificationDeliveriesAsync(order.Id);

        const string foreignTenantId = "00000000-0000-0000-0000-000000000099";
        using var foreign = harness.CreateOrderClient(claimedTenantId: foreignTenantId, subject: "foreign-user");
        foreign.DefaultRequestHeaders.Remove("X-TenantId");
        foreign.DefaultRequestHeaders.Add("X-TenantId", foreignTenantId);
        using var read = await foreign.GetAsync($"/orders/{order!.Id}");
        using var retry = await foreign.PostAsJsonAsync($"/orders/{order.Id}/payment-retry", new { RequestId = "foreign-retry", PaymentMethodToken = "pm_foreign" });

        Assert.False(read.IsSuccessStatusCode);
        Assert.False(retry.IsSuccessStatusCode);
        Assert.Equal(ordersBeforeForeignRequest, await harness.CountOrdersAsync());
        Assert.Equal(paymentsBeforeForeignRequest, await harness.CountPaymentsAsync());
        Assert.Equal(deliveriesBeforeForeignRequest, await harness.CountNotificationDeliveriesAsync(order.Id));
    }

    /// <summary>Confirms that immediate and delayed cross-service outcomes converge to one confirmed order.</summary>
    [Fact]
    public async Task PaymentAndStockOutcomes_AnyArrivalOrder_ConfirmOnceAndPublishOneNotification()
    {
        var immediate = await CreateOrderAsync();
        var delayed = await CreateOrderAsync();

        await ApplyStockAsync(immediate, backordered: false, "stock-immediate");
        await ApplyCaptureAsync(immediate, "payment-immediate");
        await ApplyCaptureAsync(delayed, "payment-delayed");
        await ApplyStockAsync(delayed, backordered: false, "stock-delayed");

        Order immediatePersisted = await ReadAsync(immediate.Id);
        Order delayedPersisted = await ReadAsync(delayed.Id);
        AssertConfirmed(immediatePersisted);
        AssertConfirmed(delayedPersisted);

        // Redelivery of either outcome must not create another terminal transition or notification.
        var bus = Substitute.For<IMessageBus>();
        await ApplyCaptureAsync(immediate, "payment-immediate", configuredBus: bus);
        await bus.DidNotReceive().PublishAsync(Arg.Any<OrderConfirmedIntegrationEvent>());
    }

    /// <summary>Proves safe failure/retry, stock rejection, and ceiling enforcement survive persisted event delivery.</summary>
    [Fact]
    public async Task FailureRetryAndSupplyOutcomes_ProduceReadableSafeTerminalStates()
    {
        var retryable = await CreateOrderAsync();
        var rejected = await CreateOrderAsync();
        var overCeiling = await CreateOrderAsync();

        await ApplyFailureAsync(retryable, "generic-decline", "Use another method.", "payment-failed");
        Order awaitingRetry = await ReadAsync(retryable.Id);
        Assert.Equal(PaymentState.ActionRequired, awaitingRetry.PaymentState);
        Assert.Equal(OrderFailureReason.PaymentActionRequired, awaitingRetry.FailureReason);
        Assert.Equal("Use another method.", awaitingRetry.ActionText);
        Assert.True(await BeginRetryAsync(retryable.Id, "retry-once"));
        Assert.False(await BeginRetryAsync(retryable.Id, "retry-once"));

        await ApplyStockRejectedAsync(rejected, "stock-rejected");
        Order cancelled = await ReadAsync(rejected.Id);
        Assert.Equal(OrderStatus.Cancelled, cancelled.Status);
        Assert.Equal(OrderFailureReason.StockRejected, cancelled.FailureReason);

        await ApplyStockAsync(overCeiling, backordered: false, "stock-over-ceiling");
        await ApplyCaptureAsync(overCeiling, "payment-over-ceiling", amount: 41m);
        Order neverOvercharged = await ReadAsync(overCeiling.Id);
        Assert.Equal(PaymentState.Pending, neverOvercharged.PaymentState);
        Assert.Equal(OrderStatus.Pending, neverOvercharged.Status);
    }

    /// <summary>Proves the bounded backorder paths converge to confirmation, rejection, or human escalation.</summary>
    [Fact]
    public async Task BackorderOutcomes_RepriceOrExpireWithoutExceedingTheAuthorizedCeiling()
    {
        var withinCeiling = await CreateOrderAsync();
        var aboveCeiling = await CreateOrderAsync();
        var expiredAfterCapture = await CreateOrderAsync();

        await ApplyStockAsync(withinCeiling, backordered: true, "stock-backorder-ok");
        await ApplyCaptureAsync(withinCeiling, "payment-backorder-ok");
        await ApplyReadyAndPriceAsync(withinCeiling, withinCeiling.AuthorizedAmount, withinCeiling.AuthorizedAmount, "backorder-ok");
        AssertConfirmed(await ReadAsync(withinCeiling.Id));

        await ApplyStockAsync(aboveCeiling, backordered: true, "stock-backorder-high");
        await ApplyCaptureAsync(aboveCeiling, "payment-backorder-high");
        await ApplyReadyAndPriceAsync(aboveCeiling, 41m, aboveCeiling.AuthorizedAmount, "backorder-high");
        Order paidUnfulfillable = await ReadAsync(aboveCeiling.Id);
        Assert.Equal(OrderStatus.PaidUnfulfillable, paidUnfulfillable.Status);
        Assert.Equal(OrderFailureReason.PriceExceededAuthorization, paidUnfulfillable.FailureReason);
        Assert.True(paidUnfulfillable.RequiresHumanDecision);

        await ApplyStockAsync(expiredAfterCapture, backordered: true, "stock-backorder-expired");
        await ApplyCaptureAsync(expiredAfterCapture, "payment-backorder-expired");
        await ApplyExpiredAsync(expiredAfterCapture, "backorder-expired");
        Order expired = await ReadAsync(expiredAfterCapture.Id);
        Assert.Equal(OrderStatus.PaidUnfulfillable, expired.Status);
        Assert.Equal(OrderFailureReason.BackorderExpired, expired.FailureReason);
        Assert.True(expired.RequiresHumanDecision);
    }

    private async Task<Order> CreateOrderAsync()
    {
        string connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(OrderDbContext), "Order.Host");
        var order = Order.Create(Guid.NewGuid(), "cross-service-owner", Guid.NewGuid(), TenantId,
            [new OrderLine(Guid.NewGuid(), "Platform priced product", 2, 20m)], 40m, "USD", Guid.NewGuid().ToString("N"));
        await using var context = CreateContext(connectionString);
        context.Orders.Add(order);
        await context.SaveChangesAsync();
        return order;
    }

    private async Task ApplyStockAsync(Order order, bool backordered, string key)
    {
        string connectionString = fixture.GetDatabaseConnectionString("testdb_orderdbcontext");
        await using var context = CreateContext(connectionString);
        var bus = Substitute.For<IMessageBus>();
        await StockReservedV2Handler.Handle(new StockReservedV2IntegrationEvent
        {
            OrderId = order.Id, SourceId = order.Id, TenantId = TenantId, IdempotencyKey = key,
            SourceCorrelationId = order.CheckoutCorrelationId,
            Lines = [new StockReservationLine { ProductId = order.Lines[0].ProductId, RequestedQuantity = 2, BackorderedQuantity = backordered ? 1 : 0 }],
        }, Repository(context), TenantContext(), new UnitOfWork<OrderDbContext>(context), bus, CancellationToken.None);
    }

    private async Task ApplyCaptureAsync(Order order, string key, decimal? amount = null, IMessageBus? configuredBus = null)
    {
        await using var context = CreateContext(fixture.GetDatabaseConnectionString("testdb_orderdbcontext"));
        await PaymentCapturedV2Handler.Handle(new PaymentCapturedV2IntegrationEvent
        {
            OrderId = order.Id, PaymentId = Guid.NewGuid(), TenantId = TenantId, Amount = amount ?? order.AuthorizedAmount,
            AuthorizedAmount = order.AuthorizedAmount, Currency = order.Currency, RequestId = key, SourceCorrelationId = order.CheckoutCorrelationId,
        }, Repository(context), TenantContext(), new UnitOfWork<OrderDbContext>(context), configuredBus ?? Substitute.For<IMessageBus>(), CancellationToken.None);
    }

    private async Task ApplyFailureAsync(Order order, string category, string action, string key)
    {
        await using var context = CreateContext(fixture.GetDatabaseConnectionString("testdb_orderdbcontext"));
        await PaymentFailedV2Handler.Handle(new PaymentFailedV2IntegrationEvent
        {
            OrderId = order.Id, PaymentId = Guid.NewGuid(), TenantId = TenantId, AuthorizedAmount = order.AuthorizedAmount,
            Currency = order.Currency, DeclineCategory = category, ActionText = action, RequestId = key, SourceCorrelationId = order.CheckoutCorrelationId,
        }, Repository(context), TenantContext(), new UnitOfWork<OrderDbContext>(context), Substitute.For<IMessageBus>(), CancellationToken.None);
    }

    private async Task ApplyStockRejectedAsync(Order order, string key)
    {
        await using var context = CreateContext(fixture.GetDatabaseConnectionString("testdb_orderdbcontext"));
        await StockReservationRejectedV2Handler.Handle(new StockReservationRejectedV2IntegrationEvent
        {
            OrderId = order.Id, SourceId = order.Id, TenantId = TenantId, IdempotencyKey = key, SourceCorrelationId = order.CheckoutCorrelationId,
        }, Repository(context), TenantContext(), new UnitOfWork<OrderDbContext>(context), Substitute.For<IMessageBus>(), CancellationToken.None);
    }

    private async Task ApplyReadyAndPriceAsync(Order order, decimal amount, decimal ceiling, string key)
    {
        await using (var readyContext = CreateContext(fixture.GetDatabaseConnectionString("testdb_orderdbcontext")))
        {
            await BackorderReadyHandler.Handle(new BackorderReadyIntegrationEvent { OrderId = order.Id, TenantId = TenantId, IdempotencyKey = key, SourceCorrelationId = order.CheckoutCorrelationId }, Repository(readyContext), TenantContext(), new UnitOfWork<OrderDbContext>(readyContext), Substitute.For<IMessageBus>(), CancellationToken.None);
        }

        await using var priceContext = CreateContext(fixture.GetDatabaseConnectionString("testdb_orderdbcontext"));
        await BackorderPriceCheckedHandler.Handle(new BackorderPriceCheckedIntegrationEvent
        {
            OrderId = order.Id, TenantId = TenantId, Amount = amount, AuthorizedAmount = ceiling, Currency = "USD",
            IsWithinAuthorizedAmount = amount <= ceiling, RequestId = key + "-price", SourceCorrelationId = order.CheckoutCorrelationId,
        }, Repository(priceContext), TenantContext(), new UnitOfWork<OrderDbContext>(priceContext), Substitute.For<IMessageBus>(), CancellationToken.None);
    }

    private async Task ApplyExpiredAsync(Order order, string key)
    {
        await using var context = CreateContext(fixture.GetDatabaseConnectionString("testdb_orderdbcontext"));
        await BackorderExpiredHandler.Handle(new BackorderExpiredIntegrationEvent { OrderId = order.Id, TenantId = TenantId, IdempotencyKey = key, SourceCorrelationId = order.CheckoutCorrelationId }, Repository(context), TenantContext(), new UnitOfWork<OrderDbContext>(context), Substitute.For<IMessageBus>(), CancellationToken.None);
    }

    private async Task<Order> ReadAsync(Guid orderId)
    {
        await using var context = CreateContext(fixture.GetDatabaseConnectionString("testdb_orderdbcontext"));
        return await context.Orders.SingleAsync(order => order.Id == orderId);
    }

    private async Task<bool> BeginRetryAsync(Guid orderId, string requestId)
    {
        await using var context = CreateContext(fixture.GetDatabaseConnectionString("testdb_orderdbcontext"));
        var order = await context.Orders.SingleAsync(candidate => candidate.Id == orderId);
        bool accepted = order.BeginRetry(requestId);
        await context.SaveChangesAsync();
        return accepted;
    }

    private static OrderDbContext CreateContext(string connectionString) => new(
        new DbContextOptionsBuilder<OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .UseTeckCloudTenant(TenantId)
            .Options,
        null!);

    private static GenericWriteRepository<Order, Guid, OrderDbContext> Repository(OrderDbContext context) => new(context, new HttpContextAccessor());

    private static ITenantInfo TenantContext()
    {
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns(TenantId);
        return tenant;
    }

    private static void AssertConfirmed(Order order)
    {
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(PaymentState.Captured, order.PaymentState);
        Assert.Equal(StockState.Reserved, order.StockState);
        Assert.False(order.RequiresHumanDecision);
    }
}
