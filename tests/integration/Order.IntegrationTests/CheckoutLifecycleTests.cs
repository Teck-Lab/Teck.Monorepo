using Microsoft.EntityFrameworkCore;
using Orders.Application.Orders.Mapping;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Orders.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class CheckoutLifecycleTests(SharedTestcontainersFixture fixture)
{
    [Fact]
    public async Task DelayedPaymentAndStockOutcomes_PersistTheSameConfirmedState()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Orders.Application.Database.OrderDbContext), "Order.Host");
        var options = new DbContextOptionsBuilder<Orders.Application.Database.OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .Options;
        var order = Order.Create(Guid.NewGuid(), "subject-lifecycle", Guid.NewGuid(), MockBearerAuthenticationHandler.TestTenantId, [new OrderLine(Guid.NewGuid(), "Widget", 2, 10m)], 25m, "USD", Guid.NewGuid().ToString("N"));

        await using (var write = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
        }

        order.ApplyStockReserved("stock-lifecycle", "checkout-lifecycle");
        order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-lifecycle", "checkout-lifecycle");
        await using (var write = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            write.Attach(order);
            write.Entry(order).State = EntityState.Modified;
            await write.SaveChangesAsync();
        }

        await using var read = new Orders.Application.Database.OrderDbContext(options, null!);
        var persisted = await read.Orders.SingleAsync(saved => saved.Id == order.Id);
        Assert.Equal(OrderStatus.Confirmed, persisted.Status);
        Assert.Equal(PaymentState.Captured, persisted.PaymentState);
        Assert.Equal(StockState.Reserved, persisted.StockState);
    }

    [Fact]
    public async Task BackorderedReservation_PersistsWaitThenConfirmsAfterReadyAndAcceptedPrice()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Orders.Application.Database.OrderDbContext), "Order.Host");
        var options = new DbContextOptionsBuilder<Orders.Application.Database.OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .Options;
        var order = Order.Create(Guid.NewGuid(), "subject-lifecycle", Guid.NewGuid(), MockBearerAuthenticationHandler.TestTenantId, [new OrderLine(Guid.NewGuid(), "Widget", 2, 10m)], 25m, "USD", Guid.NewGuid().ToString("N"));

        await using (var write = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
        }

        order.ApplyStockReserved(hasOutstandingBackorder: true, "stock-backordered", "checkout-lifecycle");
        order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-lifecycle", "checkout-lifecycle");
        await PersistAsync(order, options);

        await using (var read = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            var waiting = await read.Orders.SingleAsync(saved => saved.Id == order.Id);
            Assert.Equal(OrderStatus.Pending, waiting.Status);
            Assert.Equal(PaymentState.Captured, waiting.PaymentState);
            Assert.Equal(StockState.Backordered, waiting.StockState);
        }

        Assert.True(order.ApplyBackorderReady("backorder-ready", "checkout-lifecycle"));
        order.ApplyBackorderPriceChecked(true, "price-accepted", "checkout-lifecycle");
        await PersistAsync(order, options);

        await using var confirmedRead = new Orders.Application.Database.OrderDbContext(options, null!);
        var confirmed = await confirmedRead.Orders.SingleAsync(saved => saved.Id == order.Id);
        Assert.Equal(OrderStatus.Confirmed, confirmed.Status);
        Assert.Equal(StockState.Reserved, confirmed.StockState);
    }

    [Fact]
    public async Task ReadyBeforeBackorderedReservation_PersistsThePendingReadyFactAcrossReload()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Orders.Application.Database.OrderDbContext), "Order.Host");
        var options = new DbContextOptionsBuilder<Orders.Application.Database.OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .Options;
        var order = CreateLifecycleOrder();
        const string readyKey = "ready|key";
        const string readyCorrelation = "ready|correlation";

        await using (var write = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
        }

        Assert.True(order.ApplyBackorderReady(readyKey, readyCorrelation));
        await PersistAsync(order, options);

        Order reloaded;
        await using (var read = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            reloaded = await read.Orders.SingleAsync(saved => saved.Id == order.Id);
        }

        Assert.Equal(StockState.Pending, reloaded.StockState);
        Assert.DoesNotContain(readyKey, reloaded.ProcessedTransitionKeys, StringComparison.Ordinal);
        Assert.Null(reloaded.ApplyStockReserved(hasOutstandingBackorder: true, "reservation-later", "reservation-correlation"));
        Assert.True(reloaded.TryConsumePendingBackorderReady(out var consumedKey, out var consumedCorrelation));
        Assert.Equal(readyKey, consumedKey);
        Assert.Equal(readyCorrelation, consumedCorrelation);
        await PersistAsync(reloaded, options);

        await using var finalRead = new Orders.Application.Database.OrderDbContext(options, null!);
        var persisted = await finalRead.Orders.SingleAsync(saved => saved.Id == order.Id);
        Assert.Equal(StockState.AwaitingPriceCheck, persisted.StockState);
        Assert.DoesNotContain("pending-backorder-ready:", persisted.ProcessedTransitionKeys, StringComparison.Ordinal);
    }

    [Fact]
    public async Task BackorderedOverCeilingPrice_PersistsPaidUnfulfillableState()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Orders.Application.Database.OrderDbContext), "Order.Host");
        var options = new DbContextOptionsBuilder<Orders.Application.Database.OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .Options;
        var order = Order.Create(Guid.NewGuid(), "subject-lifecycle", Guid.NewGuid(), MockBearerAuthenticationHandler.TestTenantId, [new OrderLine(Guid.NewGuid(), "Widget", 2, 10m)], 25m, "USD", Guid.NewGuid().ToString("N"));

        await using (var write = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
        }

        order.ApplyStockReserved(hasOutstandingBackorder: true, "stock-backordered", "checkout-lifecycle");
        Assert.True(order.ApplyBackorderReady("backorder-ready", "checkout-lifecycle"));
        order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-lifecycle", "checkout-lifecycle");
        order.ApplyBackorderPriceChecked(false, "price-over-ceiling", "checkout-lifecycle");
        await PersistAsync(order, options);

        await using var read = new Orders.Application.Database.OrderDbContext(options, null!);
        var persisted = await read.Orders.SingleAsync(saved => saved.Id == order.Id);
        Assert.Equal(OrderStatus.PaidUnfulfillable, persisted.Status);
        Assert.Equal(OrderFailureReason.PriceExceededAuthorization, persisted.FailureReason);
        Assert.Equal(StockState.Rejected, persisted.StockState);
        Assert.True(persisted.RequiresHumanDecision);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public async Task BackorderedOverCeilingTransitions_PersistHumanDecisionForEitherTerminalDeliveryOrderAndRedelivery(bool paymentArrivesFirst)
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Orders.Application.Database.OrderDbContext), "Order.Host");
        var options = new DbContextOptionsBuilder<Orders.Application.Database.OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .Options;
        var order = CreateLifecycleOrder();
        const string stockKey = "stock-backordered";
        const string readyKey = "backorder-ready";
        const string paymentKey = "payment-captured";
        const string priceKey = "price-over-ceiling";

        var write = new Orders.Application.Database.OrderDbContext(options, null!);
        await using (write.ConfigureAwait(false))
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
        }

        await ApplyAndPersistAsync(options, order.Id, current => current.ApplyStockReserved(hasOutstandingBackorder: true, stockKey, current.CheckoutCorrelationId));
        await ApplyAndPersistAsync(options, order.Id, current => current.ApplyBackorderReady(readyKey, current.CheckoutCorrelationId));

        var paymentId = Guid.NewGuid();
        Action<Order> capture = current => current.ApplyPaymentCaptured(paymentId, current.AuthorizedAmount, paymentKey, current.CheckoutCorrelationId);
        Action<Order> price = current => current.ApplyBackorderPriceChecked(withinCeiling: false, priceKey, current.CheckoutCorrelationId);

        if (paymentArrivesFirst)
        {
            await ApplyAndPersistAsync(options, order.Id, capture);
            await ApplyAndPersistAsync(options, order.Id, price);
        }
        else
        {
            await ApplyAndPersistAsync(options, order.Id, price);
            await ApplyAndPersistAsync(options, order.Id, capture);
        }

        await ApplyAndPersistAsync(options, order.Id, capture);
        await ApplyAndPersistAsync(options, order.Id, price);

        var read = new Orders.Application.Database.OrderDbContext(options, null!);
        await using (read.ConfigureAwait(false))
        {
            var persisted = await read.Orders.SingleAsync(saved => saved.Id == order.Id);
            Assert.Equal(OrderStatus.PaidUnfulfillable, persisted.Status);
            Assert.Equal(OrderFailureReason.PriceExceededAuthorization, persisted.FailureReason);
            Assert.True(persisted.RequiresHumanDecision);
        }
    }

    [Fact]
    public async Task BackorderedWaitTimeout_PersistsPaidUnfulfillableState()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Orders.Application.Database.OrderDbContext), "Order.Host");
        var options = new DbContextOptionsBuilder<Orders.Application.Database.OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .Options;
        var order = Order.Create(Guid.NewGuid(), "subject-lifecycle", Guid.NewGuid(), MockBearerAuthenticationHandler.TestTenantId, [new OrderLine(Guid.NewGuid(), "Widget", 2, 10m)], 25m, "USD", Guid.NewGuid().ToString("N"));

        await using (var write = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
        }

        order.ApplyStockReserved(hasOutstandingBackorder: true, "stock-backordered", "checkout-lifecycle");
        order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-lifecycle", "checkout-lifecycle");
        order.ApplyBackorderExpired("backorder-expired", "checkout-lifecycle");
        await PersistAsync(order, options);

        await using var read = new Orders.Application.Database.OrderDbContext(options, null!);
        var persisted = await read.Orders.SingleAsync(saved => saved.Id == order.Id);
        Assert.Equal(OrderStatus.PaidUnfulfillable, persisted.Status);
        Assert.Equal(OrderFailureReason.BackorderExpired, persisted.FailureReason);
        Assert.Equal(StockState.Expired, persisted.StockState);
        Assert.True(persisted.RequiresHumanDecision);
    }

    [Fact]
    public async Task RetryLedger_PersistsDelimiterBearingIdsAndSuppressesAReplayAfterLaterRetryKeys()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Orders.Application.Database.OrderDbContext), "Order.Host");
        var options = new DbContextOptionsBuilder<Orders.Application.Database.OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .Options;
        var order = Order.Create(Guid.NewGuid(), "subject-lifecycle", Guid.NewGuid(), MockBearerAuthenticationHandler.TestTenantId, [new OrderLine(Guid.NewGuid(), "Widget", 2, 10m)], 25m, "USD", Guid.NewGuid().ToString("N"));

        await using (var write = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
        }

        Assert.IsType<Orders.Domain.DomainEvents.OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed-a", "checkout-lifecycle"));
        const string firstRequestId = "retry-a|payment-retry:retry-b";
        const string secondRequestId = "retry-b";
        Assert.True(order.BeginRetry(firstRequestId));
        await PersistAsync(order, options);

        Order reloaded;
        await using (var read = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            reloaded = await read.Orders.SingleAsync(saved => saved.Id == order.Id);
        }

        Assert.Equal(PaymentState.Pending, reloaded.PaymentState);
        Assert.Equal(25m, reloaded.AuthorizedAmount);
        Assert.IsType<Orders.Domain.DomainEvents.OrderPaymentActionRequired>(reloaded.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed-b", "checkout-lifecycle"));
        Assert.True(reloaded.BeginRetry(secondRequestId));
        Assert.IsType<Orders.Domain.DomainEvents.OrderPaymentActionRequired>(reloaded.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed-c", "checkout-lifecycle"));
        Assert.False(reloaded.BeginRetry(firstRequestId));
        Assert.Equal(secondRequestId, reloaded.RetryRequestId);
        Assert.Equal(PaymentState.ActionRequired, reloaded.PaymentState);
        Assert.Equal(25m, reloaded.AuthorizedAmount);
        await PersistAsync(reloaded, options);

        await using var finalRead = new Orders.Application.Database.OrderDbContext(options, null!);
        var persisted = await finalRead.Orders.SingleAsync(saved => saved.Id == order.Id);
        Assert.True(persisted.HasRecordedRetryRequest(firstRequestId));
        Assert.True(persisted.HasRecordedRetryRequest(secondRequestId));
        Assert.Equal(MockBearerAuthenticationHandler.TestTenantId, persisted.TenantId);
    }

    [Fact]
    public async Task PaymentFailure_PersistsAndMapsSafeActionForLaterReadback()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Orders.Application.Database.OrderDbContext), "Order.Host");
        var options = new DbContextOptionsBuilder<Orders.Application.Database.OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .Options;
        var order = Order.Create(Guid.NewGuid(), "subject-lifecycle", Guid.NewGuid(), MockBearerAuthenticationHandler.TestTenantId, [new OrderLine(Guid.NewGuid(), "Widget", 2, 10m)], 25m, "USD", Guid.NewGuid().ToString("N"));

        await using (var write = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            write.Orders.Add(order);
            await write.SaveChangesAsync();
        }

        order.ApplyPaymentFailure("generic-decline", "Use another payment method.", "payment-failure", "checkout-lifecycle");
        await using (var write = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            write.Attach(order);
            write.Entry(order).State = EntityState.Modified;
            await write.SaveChangesAsync();
        }

        await using var read = new Orders.Application.Database.OrderDbContext(options, null!);
        var persisted = await read.Orders.SingleAsync(saved => saved.Id == order.Id);
        var dto = persisted.ToDto();

        Assert.Equal(OrderFailureReason.PaymentActionRequired, persisted.FailureReason);
        Assert.Equal("Use another payment method.", persisted.ActionText);
        Assert.Equal("PaymentActionRequired", dto.FailureReason);
        Assert.Equal("Use another payment method.", dto.ActionText);
    }

    [Fact]
    public async Task CurrentLifecycleActions_PersistAndMapForRetryCaptureAndTerminalStates()
    {
        var connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(Orders.Application.Database.OrderDbContext), "Order.Host");
        var options = new DbContextOptionsBuilder<Orders.Application.Database.OrderDbContext>()
            .UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Order.Host"))
            .Options;
        var retryPending = CreateLifecycleOrder();
        var capturedAwaitingStock = CreateLifecycleOrder();
        var cancelled = CreateLifecycleOrder();
        var expired = CreateLifecycleOrder();
        var rejected = CreateLifecycleOrder();
        var paidUnfulfillable = CreateLifecycleOrder();

        await using (var write = new Orders.Application.Database.OrderDbContext(options, null!))
        {
            write.Orders.AddRange(retryPending, capturedAwaitingStock, cancelled, expired, rejected, paidUnfulfillable);
            await write.SaveChangesAsync();
        }

        Assert.IsType<Orders.Domain.DomainEvents.OrderPaymentActionRequired>(retryPending.ApplyPaymentFailure("generic-decline", "Use another payment method.", "retry-failure", "checkout-lifecycle"));
        Assert.True(retryPending.BeginRetry("retry-1"));

        Assert.IsType<Orders.Domain.DomainEvents.OrderPaymentActionRequired>(capturedAwaitingStock.ApplyPaymentFailure("generic-decline", "Use another payment method.", "capture-failure", "checkout-lifecycle"));
        Assert.True(capturedAwaitingStock.BeginRetry("retry-2"));
        Assert.Null(capturedAwaitingStock.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "capture-after-retry", "checkout-lifecycle"));

        Assert.IsType<Orders.Domain.DomainEvents.OrderRejected>(cancelled.ApplyStockRejected("stock-rejected", "checkout-lifecycle", "Stock is unavailable."));
        Assert.IsType<Orders.Domain.DomainEvents.OrderCancelled>(expired.ApplyBackorderExpired("backorder-expired", "checkout-lifecycle"));
        Assert.IsType<Orders.Domain.DomainEvents.OrderRejected>(rejected.ApplyBackorderPriceChecked(false, "price-rejected", "checkout-lifecycle"));
        Assert.Null(paidUnfulfillable.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "paid-first", "checkout-lifecycle"));
        Assert.IsType<Orders.Domain.DomainEvents.OrderRejected>(paidUnfulfillable.ApplyStockRejected("paid-stock-rejected", "checkout-lifecycle", "Stock is unavailable."));
        await PersistAsync(retryPending, options);
        await PersistAsync(capturedAwaitingStock, options);
        await PersistAsync(cancelled, options);
        await PersistAsync(expired, options);
        await PersistAsync(rejected, options);
        await PersistAsync(paidUnfulfillable, options);

        await using var read = new Orders.Application.Database.OrderDbContext(options, null!);
        var persisted = await read.Orders.Where(order => new[] { retryPending.Id, capturedAwaitingStock.Id, cancelled.Id, expired.Id, rejected.Id, paidUnfulfillable.Id }.Contains(order.Id)).ToDictionaryAsync(order => order.Id);

        AssertReadableAction(persisted[retryPending.Id].ToDto(), OrderStatus.Pending, PaymentState.Pending, OrderFailureReason.None, string.Empty);
        AssertReadableAction(persisted[capturedAwaitingStock.Id].ToDto(), OrderStatus.Pending, PaymentState.Captured, OrderFailureReason.None, string.Empty);
        AssertReadableAction(persisted[cancelled.Id].ToDto(), OrderStatus.Cancelled, PaymentState.Pending, OrderFailureReason.StockRejected, "Stock is unavailable.");
        AssertReadableAction(persisted[expired.Id].ToDto(), OrderStatus.Cancelled, PaymentState.Pending, OrderFailureReason.BackorderExpired, "Your backordered items were not available in time.");
        AssertReadableAction(persisted[rejected.Id].ToDto(), OrderStatus.Rejected, PaymentState.Pending, OrderFailureReason.PriceExceededAuthorization, "The current price exceeds your authorized amount.");
        AssertReadableAction(persisted[paidUnfulfillable.Id].ToDto(), OrderStatus.PaidUnfulfillable, PaymentState.Captured, OrderFailureReason.StockRejected, "Stock is unavailable.");
    }

    private static async Task PersistAsync(Order order, DbContextOptions<Orders.Application.Database.OrderDbContext> options)
    {
        await using var write = new Orders.Application.Database.OrderDbContext(options, null!);
        write.Attach(order);
        write.Entry(order).State = EntityState.Modified;
        await write.SaveChangesAsync();
    }

    private static async Task ApplyAndPersistAsync(
        DbContextOptions<Orders.Application.Database.OrderDbContext> options,
        Guid orderId,
        Action<Order> apply)
    {
        var context = new Orders.Application.Database.OrderDbContext(options, null!);
        await using (context.ConfigureAwait(false))
        {
            var order = await context.Orders.SingleAsync(current => current.Id == orderId).ConfigureAwait(false);
            apply(order);
            await context.SaveChangesAsync().ConfigureAwait(false);
        }
    }

    private static Order CreateLifecycleOrder() => Order.Create(
        Guid.NewGuid(),
        "subject-lifecycle",
        Guid.NewGuid(),
        MockBearerAuthenticationHandler.TestTenantId,
        [new OrderLine(Guid.NewGuid(), "Widget", 2, 10m)],
        25m,
        "USD",
        Guid.NewGuid().ToString("N"));

    private static void AssertReadableAction(
        Orders.Application.Orders.Responses.OrderDto dto,
        OrderStatus status,
        PaymentState paymentState,
        OrderFailureReason failureReason,
        string actionText)
    {
        Assert.Equal(status.Name, dto.Status);
        Assert.Equal(paymentState.Name, dto.PaymentStatus);
        Assert.Equal(failureReason.Name, dto.FailureReason);
        Assert.Equal(actionText, dto.ActionText);
    }
}
