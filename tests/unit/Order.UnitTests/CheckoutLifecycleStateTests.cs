using Ardalis.Specification;
using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Orders.Application.Orders.EventHandlers.IntegrationEvents;
using Orders.Application.Orders.Features.RetryPayment.V1;
using Orders.Application.Orders;
using Orders.Domain.DomainEvents;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Messaging.MultiTenant;
using SharedKernel.Infrastructure.MultiTenant;
using Wolverine;
using Xunit;

namespace Orders.UnitTests;

public sealed class CheckoutLifecycleStateTests
{
    [Fact]
    public void PaymentThenStock_ConfirmsExactlyOnce()
    {
        var order = CreateOrder();

        Assert.Null(order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-1", "checkout-1"));
        var confirmed = Assert.IsType<OrderConfirmed>(order.ApplyStockReserved("stock-1", "checkout-1"));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(PaymentState.Captured, order.PaymentState);
        Assert.Equal(StockState.Reserved, order.StockState);
        Assert.Equal(order.Id, confirmed.OrderId);
        Assert.Null(order.ApplyStockReserved("stock-1", "checkout-1"));
    }

    [Fact]
    public void StockThenPayment_UsesTheSameConfirmationTransition()
    {
        var order = CreateOrder();

        Assert.Null(order.ApplyStockReserved("stock-1", "checkout-1"));
        var confirmed = Assert.IsType<OrderConfirmed>(order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-1", "checkout-1"));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(order.AuthorizedAmount, confirmed.AuthorizedAmount);
    }

    [Fact]
    public async Task V2BackorderedReservation_WaitsForReadyAndAcceptedPriceBeforeConfirmation()
    {
        var order = CreateOrder();
        var orders = Substitute.For<IGenericWriteRepository<Order, Guid>>();
        orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Order?>(order));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        var bus = Substitute.For<IMessageBus>();

        await StockReservedV2Handler.Handle(
            new StockReservedV2IntegrationEvent
            {
                OrderId = order.Id,
                TenantId = order.TenantId,
                IdempotencyKey = "stock-backordered",
                SourceCorrelationId = "checkout-1",
                Lines = [new StockReservationLine(order.Lines[0].ProductId, order.Lines[0].Quantity, 1)],
            },
            orders,
            Tenant(order.TenantId),
            unitOfWork,
            bus,
            CancellationToken.None);

        Assert.Equal(StockState.Backordered, order.StockState);
        Assert.Null(order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-1", "checkout-1"));
        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Null(order.ApplyBackorderPriceChecked(true, "price-before-ready", "checkout-1"));
        Assert.Equal(StockState.Backordered, order.StockState);
        Assert.True(order.ApplyBackorderReady("backorder-ready", "checkout-1"));
        var confirmed = Assert.IsType<OrderConfirmed>(order.ApplyBackorderPriceChecked(true, "price-accepted", "checkout-1"));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(StockState.Reserved, order.StockState);
        Assert.Equal(order.Id, confirmed.OrderId);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public void BackorderReadyAndReservation_AggregateDeliveryOrdersConvergeWithDelimiterSafePendingFact()
    {
        const string readyKey = "ready|key";
        const string readyCorrelation = "ready|correlation";
        const string reservationKey = "reservation-key";

        var reservationFirst = CreateOrder();
        Assert.Null(reservationFirst.ApplyStockReserved(hasOutstandingBackorder: true, reservationKey, "reservation-correlation"));
        Assert.True(reservationFirst.ApplyBackorderReady(readyKey, readyCorrelation));

        var readyFirst = CreateOrder();
        Assert.True(readyFirst.ApplyBackorderReady(readyKey, readyCorrelation));
        Assert.Equal(StockState.Pending, readyFirst.StockState);
        Assert.DoesNotContain(readyKey, readyFirst.ProcessedTransitionKeys, StringComparison.Ordinal);
        Assert.DoesNotContain(readyCorrelation, readyFirst.ProcessedTransitionKeys, StringComparison.Ordinal);
        Assert.Null(readyFirst.ApplyStockReserved(hasOutstandingBackorder: true, reservationKey, "reservation-correlation"));
        Assert.True(readyFirst.TryConsumePendingBackorderReady(out var consumedKey, out var consumedCorrelation));

        Assert.Equal(StockState.AwaitingPriceCheck, reservationFirst.StockState);
        Assert.Equal(reservationFirst.StockState, readyFirst.StockState);
        Assert.Equal(readyKey, consumedKey);
        Assert.Equal(readyCorrelation, consumedCorrelation);
        Assert.False(readyFirst.ApplyBackorderReady(readyKey, readyCorrelation));
        Assert.Null(readyFirst.ApplyStockReserved(hasOutstandingBackorder: true, reservationKey, "reservation-correlation"));
        Assert.False(readyFirst.TryConsumePendingBackorderReady(out _, out _));
    }

    [Fact]
    public async Task BackorderReadyAndReservationHandlers_ConvergeAndPublishOnePriceCheckForEitherDeliveryOrder()
    {
        var reservationFirst = CreateOrder();
        var (reservationFirstOrders, reservationFirstUnitOfWork, reservationFirstBus) = CreateHandlerDependencies(reservationFirst);
        var reservationEvent = CreateBackorderedReservationEvent(reservationFirst, "reservation-first", "reservation-correlation");
        var readyEvent = CreateBackorderReadyEvent(reservationFirst, "ready-first", "ready-correlation");

        await StockReservedV2Handler.Handle(reservationEvent, reservationFirstOrders, Tenant(reservationFirst.TenantId), reservationFirstUnitOfWork, reservationFirstBus, CancellationToken.None);
        await BackorderReadyHandler.Handle(readyEvent, reservationFirstOrders, Tenant(reservationFirst.TenantId), reservationFirstUnitOfWork, reservationFirstBus, CancellationToken.None);
        await StockReservedV2Handler.Handle(reservationEvent, reservationFirstOrders, Tenant(reservationFirst.TenantId), reservationFirstUnitOfWork, reservationFirstBus, CancellationToken.None);
        await BackorderReadyHandler.Handle(readyEvent, reservationFirstOrders, Tenant(reservationFirst.TenantId), reservationFirstUnitOfWork, reservationFirstBus, CancellationToken.None);

        Assert.Equal(StockState.AwaitingPriceCheck, reservationFirst.StockState);
        await reservationFirstBus.Received(1).PublishAsync(Arg.Is<BackorderPriceCheckRequestedIntegrationEvent>(evt =>
            evt.OrderId == reservationFirst.Id &&
            evt.RequestId == "backorder-price:ready-first" &&
            evt.SourceCorrelationId == "ready-correlation"));

        var readyFirst = CreateOrder();
        var (readyFirstOrders, readyFirstUnitOfWork, readyFirstBus) = CreateHandlerDependencies(readyFirst);
        var readyFirstReservationEvent = CreateBackorderedReservationEvent(readyFirst, "reservation-later", "reservation-correlation");
        var readyFirstEvent = CreateBackorderReadyEvent(readyFirst, "ready-early", "ready-correlation");

        await BackorderReadyHandler.Handle(readyFirstEvent, readyFirstOrders, Tenant(readyFirst.TenantId), readyFirstUnitOfWork, readyFirstBus, CancellationToken.None);
        await StockReservedV2Handler.Handle(readyFirstReservationEvent, readyFirstOrders, Tenant(readyFirst.TenantId), readyFirstUnitOfWork, readyFirstBus, CancellationToken.None);
        await BackorderReadyHandler.Handle(readyFirstEvent, readyFirstOrders, Tenant(readyFirst.TenantId), readyFirstUnitOfWork, readyFirstBus, CancellationToken.None);
        await StockReservedV2Handler.Handle(readyFirstReservationEvent, readyFirstOrders, Tenant(readyFirst.TenantId), readyFirstUnitOfWork, readyFirstBus, CancellationToken.None);

        Assert.Equal(StockState.AwaitingPriceCheck, readyFirst.StockState);
        await readyFirstBus.Received(1).PublishAsync(Arg.Is<BackorderPriceCheckRequestedIntegrationEvent>(evt =>
            evt.OrderId == readyFirst.Id &&
            evt.RequestId == "backorder-price:ready-early" &&
            evt.SourceCorrelationId == "ready-correlation"));
    }

    [Fact]
    public async Task V2FullyReservedReservation_UsesTheExistingConfirmablePath()
    {
        var order = CreateOrder();
        Assert.Null(order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-1", "checkout-1"));
        var orders = Substitute.For<IGenericWriteRepository<Order, Guid>>();
        orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Order?>(order));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        var bus = Substitute.For<IMessageBus>();

        await StockReservedV2Handler.Handle(
            new StockReservedV2IntegrationEvent
            {
                OrderId = order.Id,
                TenantId = order.TenantId,
                IdempotencyKey = "stock-reserved",
                SourceCorrelationId = "checkout-1",
                Lines = [new StockReservationLine(order.Lines[0].ProductId, order.Lines[0].Quantity, 0)],
            },
            orders,
            Tenant(order.TenantId),
            unitOfWork,
            bus,
            CancellationToken.None);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(StockState.Reserved, order.StockState);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<OrderConfirmedIntegrationEvent>(evt => evt.OrderId == order.Id));
    }

    [Fact]
    public async Task PaymentCapturedHandler_WolverineEnvelopeTenant_ProcessesMatchingPayloadAndRejectsMismatch()
    {
        var order = CreateOrder();
        Assert.Null(order.ApplyStockReserved("stock-1", "checkout-1"));
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);
        var accessor = Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();
        IMultiTenantContext currentTenantContext = new MultiTenantContext<TenantDetails>(new TenantDetails { Id = "previous", Identifier = "previous", Name = "previous", IsActive = true });
        accessor.MultiTenantContext.Returns(_ => currentTenantContext);
        var setter = Substitute.For<IMultiTenantContextSetter>();
        setter.When(context => context.MultiTenantContext = Arg.Any<IMultiTenantContext>())
            .Do(call => currentTenantContext = call.Arg<IMultiTenantContext>());
        var middleware = new TenantPropagationMiddleware(
            accessor,
            setter,
            Substitute.For<ILogger<TenantPropagationMiddleware>>());
        var matching = CreatePaymentCapturedEvent(order, order.AuthorizedAmount, order.Currency, order.AuthorizedAmount, "envelope-matching");
        var matchingContext = Substitute.For<IMessageContext>();
        matchingContext.Envelope.Returns(new Envelope(matching) { TenantId = order.TenantId });

        var matchingScope = middleware.Before(matchingContext);
        try
        {
            await PaymentCapturedV2Handler.Handle(matching, orders, accessor.MultiTenantContext.TenantInfo!, unitOfWork, bus, CancellationToken.None);
        }
        finally
        {
            middleware.Finally(matchingScope);
        }

        Assert.Equal(PaymentState.Captured, order.PaymentState);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());

        var mismatched = CreatePaymentCapturedEvent(order, order.AuthorizedAmount, order.Currency, order.AuthorizedAmount, "envelope-mismatch");
        mismatched.TenantId = "foreign-tenant";
        var mismatchedContext = Substitute.For<IMessageContext>();
        mismatchedContext.Envelope.Returns(new Envelope(mismatched) { TenantId = order.TenantId });
        var mismatchedScope = middleware.Before(mismatchedContext);
        try
        {
            var exception = await Assert.ThrowsAsync<InvalidOperationException>(() =>
                PaymentCapturedV2Handler.Handle(mismatched, orders, accessor.MultiTenantContext.TenantInfo!, unitOfWork, bus, CancellationToken.None));

            Assert.Contains("does not match Wolverine envelope tenant", exception.Message, StringComparison.Ordinal);
        }
        finally
        {
            middleware.Finally(mismatchedScope);
        }

        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await orders.Received(1).FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), true, Arg.Any<CancellationToken>());
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(26)]
    public async Task V2InvalidCaptureAmount_DoesNotMutateOrConsumeTheRequest(decimal amount)
    {
        var order = CreateOrder();
        Assert.Null(order.ApplyStockReserved("stock-1", "checkout-1"));
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);
        var invalid = CreatePaymentCapturedEvent(order, amount, order.Currency, order.AuthorizedAmount, "capture-invalid");

        await PaymentCapturedV2Handler.Handle(invalid, orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);

        Assert.Equal(PaymentState.Pending, order.PaymentState);
        Assert.Equal(0m, order.CapturedAmount);
        Assert.DoesNotContain(invalid.RequestId, order.ProcessedTransitionKeys, StringComparison.Ordinal);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<OrderConfirmedIntegrationEvent>());

        await PaymentCapturedV2Handler.Handle(CreatePaymentCapturedEvent(order, 20m, order.Currency, order.AuthorizedAmount, invalid.RequestId), orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);

        Assert.Equal(PaymentState.Captured, order.PaymentState);
        Assert.Equal(20m, order.CapturedAmount);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<OrderConfirmedIntegrationEvent>(evt => evt.OrderId == order.Id));
    }

    [Fact]
    public async Task V2MismatchedCaptureAuthority_DoesNotMutateOrConsumeTheRequest()
    {
        var order = CreateOrder();
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);
        var currencyMismatch = CreatePaymentCapturedEvent(order, 20m, "EUR", order.AuthorizedAmount, "capture-currency");
        var ceilingMismatch = CreatePaymentCapturedEvent(order, 20m, order.Currency, order.AuthorizedAmount - 1m, "capture-ceiling");

        await PaymentCapturedV2Handler.Handle(currencyMismatch, orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);
        await PaymentCapturedV2Handler.Handle(ceilingMismatch, orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);

        Assert.Equal(PaymentState.Pending, order.PaymentState);
        Assert.DoesNotContain(currencyMismatch.RequestId, order.ProcessedTransitionKeys, StringComparison.Ordinal);
        Assert.DoesNotContain(ceilingMismatch.RequestId, order.ProcessedTransitionKeys, StringComparison.Ordinal);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());

        await PaymentCapturedV2Handler.Handle(CreatePaymentCapturedEvent(order, 20m, order.Currency, order.AuthorizedAmount, currencyMismatch.RequestId), orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);

        Assert.Equal(PaymentState.Captured, order.PaymentState);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task V2InvalidBackorderPriceAuthority_DoesNotMutateOrConsumeTheRequest()
    {
        var order = CreateBackorderAwaitingPriceCheck();
        Assert.Null(order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-1", "checkout-1"));
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);
        var invalid = CreateBackorderPriceEvent(order, amount: 20m, currency: "EUR", authorizedAmount: order.AuthorizedAmount, requestId: "price-invalid");
        var ceilingMismatch = CreateBackorderPriceEvent(order, amount: 20m, currency: order.Currency, authorizedAmount: order.AuthorizedAmount - 1m, requestId: "price-ceiling");

        await BackorderPriceCheckedHandler.Handle(invalid, orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);
        await BackorderPriceCheckedHandler.Handle(ceilingMismatch, orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(StockState.AwaitingPriceCheck, order.StockState);
        Assert.DoesNotContain(invalid.RequestId, order.ProcessedTransitionKeys, StringComparison.Ordinal);
        Assert.DoesNotContain(ceilingMismatch.RequestId, order.ProcessedTransitionKeys, StringComparison.Ordinal);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<OrderConfirmedIntegrationEvent>());

        await BackorderPriceCheckedHandler.Handle(CreateBackorderPriceEvent(order, amount: 20m, currency: order.Currency, authorizedAmount: order.AuthorizedAmount, requestId: invalid.RequestId), orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(StockState.Reserved, order.StockState);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<OrderConfirmedIntegrationEvent>(evt => evt.OrderId == order.Id));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    public async Task V2InvalidBackorderPriceAmount_DoesNotMutateOrConsumeTheRequest(decimal amount)
    {
        var order = CreateBackorderAwaitingPriceCheck();
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);
        var invalid = CreateBackorderPriceEvent(order, amount, order.Currency, order.AuthorizedAmount, "price-invalid");

        await BackorderPriceCheckedHandler.Handle(invalid, orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);

        Assert.Equal(StockState.AwaitingPriceCheck, order.StockState);
        Assert.DoesNotContain(invalid.RequestId, order.ProcessedTransitionKeys, StringComparison.Ordinal);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
    }

    [Fact]
    public async Task V2OverCeilingBackorderPrice_RetainsTheRejectionFlow()
    {
        var order = CreateBackorderAwaitingPriceCheck();
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);

        await BackorderPriceCheckedHandler.Handle(CreateBackorderPriceEvent(order, amount: order.AuthorizedAmount + 1m, currency: order.Currency, authorizedAmount: order.AuthorizedAmount, requestId: "price-over-ceiling", isWithinAuthorizedAmount: false), orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);

        Assert.Equal(OrderStatus.Rejected, order.Status);
        Assert.Equal(StockState.Rejected, order.StockState);
        Assert.Equal(OrderFailureReason.PriceExceededAuthorization, order.FailureReason);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<StockReleaseRequestedIntegrationEvent>(evt => evt.OrderId == order.Id));
    }

    [Fact]
    public async Task RetryHandler_ActionRequiredOrder_PublishesOnceAndPreservesCeiling()
    {
        var order = CreateOrder();
        Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed", "checkout-1"));
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);
        var identity = Substitute.For<IOrderIdentityAccessor>();
        identity.Subject.Returns(order.KeycloakSubjectId);
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns(order.TenantId);

        var result = await RetryPaymentHandler.Handle(new RetryPaymentCommand(order.Id, "retry-a", "token-a"), orders, identity, tenant, unitOfWork, bus, CancellationToken.None);

        Assert.False(result.IsError);
        Assert.Equal(PaymentState.Pending, order.PaymentState);
        Assert.Equal("retry-a", order.RetryRequestId);
        Assert.Equal(25m, order.AuthorizedAmount);
        await unitOfWork.Received(1).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<PaymentRetryRequestedIntegrationEvent>(evt =>
            evt.OrderId == order.Id &&
            evt.RequestId == "retry-a" &&
            evt.AuthorizedAmount == order.AuthorizedAmount &&
            evt.Currency == order.Currency));
    }

    [Fact]
    public async Task RetryHandler_ConfirmedOrder_DeniesWithoutMutationOrPublication()
    {
        var order = CreateOrder();
        Assert.Null(order.ApplyStockReserved("stock-1", "checkout-1"));
        Assert.IsType<OrderConfirmed>(order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-1", "checkout-1"));
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);
        var identity = Substitute.For<IOrderIdentityAccessor>();
        identity.Subject.Returns(order.KeycloakSubjectId);
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns(order.TenantId);

        var result = await RetryPaymentHandler.Handle(new RetryPaymentCommand(order.Id, "retry-terminal", "token-terminal"), orders, identity, tenant, unitOfWork, bus, CancellationToken.None);

        Assert.True(result.IsError);
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(PaymentState.Captured, order.PaymentState);
        Assert.Null(order.RetryRequestId);
        Assert.Equal(25m, order.AuthorizedAmount);
        await unitOfWork.DidNotReceive().SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.DidNotReceive().PublishAsync(Arg.Any<PaymentRetryRequestedIntegrationEvent>());
    }

    [Fact]
    public async Task RetryHandler_AdversarialRequestSequenceAThenBThenA_PublishesEachRequestOnce()
    {
        var order = CreateOrder();
        Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed-a", "checkout-1"));
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);
        var identity = Substitute.For<IOrderIdentityAccessor>();
        identity.Subject.Returns(order.KeycloakSubjectId);
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns(order.TenantId);
        const string firstRequestId = "retry-a|payment-retry:retry-b";
        const string secondRequestId = "retry-b";

        var first = await RetryPaymentHandler.Handle(new RetryPaymentCommand(order.Id, firstRequestId, "token-a"), orders, identity, tenant, unitOfWork, bus, CancellationToken.None);
        Assert.False(first.IsError);
        Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed-b", "checkout-1"));

        var second = await RetryPaymentHandler.Handle(new RetryPaymentCommand(order.Id, secondRequestId, "token-b"), orders, identity, tenant, unitOfWork, bus, CancellationToken.None);
        Assert.False(second.IsError);
        Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed-c", "checkout-1"));

        var replay = await RetryPaymentHandler.Handle(new RetryPaymentCommand(order.Id, firstRequestId, "token-a-replay"), orders, identity, tenant, unitOfWork, bus, CancellationToken.None);

        Assert.False(replay.IsError);
        Assert.Equal(PaymentState.ActionRequired, order.PaymentState);
        Assert.Equal(secondRequestId, order.RetryRequestId);
        Assert.Equal(25m, order.AuthorizedAmount);
        await unitOfWork.Received(2).SaveChangesAsync(Arg.Any<CancellationToken>());
        await bus.Received(1).PublishAsync(Arg.Is<PaymentRetryRequestedIntegrationEvent>(evt => evt.RequestId == firstRequestId));
        await bus.Received(1).PublishAsync(Arg.Is<PaymentRetryRequestedIntegrationEvent>(evt => evt.RequestId == secondRequestId));
    }

    [Fact]
    public void RetryLedger_EncodesDelimiterBearingRequestIdsWithoutAliasing()
    {
        var order = CreateOrder();
        const string firstRequestId = "retry-a|payment-retry:retry-b";
        const string secondRequestId = "retry-b";

        Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed-a", "checkout-1"));
        Assert.True(order.BeginRetry(firstRequestId));
        Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed-b", "checkout-1"));
        Assert.True(order.BeginRetry(secondRequestId));
        Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-failed-c", "checkout-1"));

        Assert.False(order.BeginRetry(firstRequestId));
        Assert.True(order.HasRecordedRetryRequest(firstRequestId));
        Assert.True(order.HasRecordedRetryRequest(secondRequestId));
        Assert.Equal(2, order.ProcessedTransitionKeys.Split('|', StringSplitOptions.RemoveEmptyEntries).Count(key => key.StartsWith("payment-retry:", StringComparison.Ordinal)));
        Assert.DoesNotContain("payment-retry:retry-a|payment-retry:retry-b", order.ProcessedTransitionKeys, StringComparison.Ordinal);
    }

    [Fact]
    public void PaymentActionThenRetry_CanConvergeToConfirmed()
    {
        var order = CreateOrder();

        var action = Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "failed-1", "checkout-1"));
        Assert.Equal(PaymentState.ActionRequired, order.PaymentState);
        Assert.Equal("PaymentActionRequired", action.DeclineCategory);
        Assert.Equal("Use another method.", order.ActionText);
        Assert.True(order.BeginRetry("retry-1"));
        Assert.Equal(OrderFailureReason.None, order.FailureReason);
        Assert.Equal(string.Empty, order.ActionText);
        Assert.Null(order.ApplyStockReserved("stock-1", "checkout-1"));

        var confirmed = Assert.IsType<OrderConfirmed>(order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-2", "checkout-1"));
        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(string.Empty, order.ActionText);
        Assert.Equal(20m, confirmed.Amount);
    }

    [Fact]
    public void RetryThenCaptureBeforeStock_ClearsResolvedPaymentAction()
    {
        var order = CreateOrder();
        Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "failure-1", "checkout-1"));
        Assert.True(order.BeginRetry("retry-1"));

        Assert.Null(order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-1", "checkout-1"));

        Assert.Equal(OrderStatus.Pending, order.Status);
        Assert.Equal(PaymentState.Captured, order.PaymentState);
        Assert.Equal(StockState.Pending, order.StockState);
        Assert.Equal(OrderFailureReason.None, order.FailureReason);
        Assert.Equal(string.Empty, order.ActionText);
    }

    [Fact]
    public void RejectedStockAndCapturedPayment_ConvergeRegardlessOfDeliveryOrder()
    {
        var captureFirst = CreateOrder();
        Assert.Null(captureFirst.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-capture-first", "checkout-1"));
        var captureFirstNotification = Assert.IsType<OrderRejected>(captureFirst.ApplyStockRejected("stock-capture-first", "checkout-1", "Not available."));
        var captureFirstDuplicate = captureFirst.ApplyStockRejected("stock-capture-first", "checkout-1", "Not available.");
        Assert.Null(captureFirstDuplicate);

        var rejectionFirst = CreateOrder();
        var initialRejection = Assert.IsType<OrderRejected>(rejectionFirst.ApplyStockRejected("stock-rejection-first", "checkout-1", "Not available."));
        var rejectionFirstNotification = Assert.IsType<OrderRejected>(rejectionFirst.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-rejection-first", "checkout-1"));
        var rejectionFirstDuplicate = rejectionFirst.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-rejection-first", "checkout-1");
        Assert.Null(rejectionFirstDuplicate);

        AssertPaidUnfulfillable(captureFirst, captureFirstNotification, OrderFailureReason.StockRejected);
        AssertPaidUnfulfillable(rejectionFirst, rejectionFirstNotification, OrderFailureReason.StockRejected);
        AssertSingleHumanDecisionNotification(captureFirst, captureFirstNotification, captureFirstDuplicate);
        AssertSingleHumanDecisionNotification(rejectionFirst, initialRejection, rejectionFirstNotification, rejectionFirstDuplicate);
        Assert.Equal("Not available.", initialRejection.ActionText);
    }

    [Fact]
    public void ExpiredBackorderAndCapturedPayment_ConvergeRegardlessOfDeliveryOrder()
    {
        var captureFirst = CreateOrder();
        Assert.Null(captureFirst.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-capture-first", "checkout-1"));
        var captureFirstNotification = Assert.IsType<OrderRejected>(captureFirst.ApplyBackorderExpired("expiry-capture-first", "checkout-1"));
        var captureFirstDuplicate = captureFirst.ApplyBackorderExpired("expiry-capture-first", "checkout-1");
        Assert.Null(captureFirstDuplicate);

        var expiryFirst = CreateOrder();
        var cancellation = Assert.IsType<OrderCancelled>(expiryFirst.ApplyBackorderExpired("expiry-first", "checkout-1"));
        var expiryFirstNotification = Assert.IsType<OrderRejected>(expiryFirst.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-expiry-first", "checkout-1"));
        var expiryFirstDuplicate = expiryFirst.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-expiry-first", "checkout-1");
        Assert.Null(expiryFirstDuplicate);

        AssertPaidUnfulfillable(captureFirst, captureFirstNotification, OrderFailureReason.BackorderExpired);
        AssertPaidUnfulfillable(expiryFirst, expiryFirstNotification, OrderFailureReason.BackorderExpired);
        AssertSingleHumanDecisionNotification(captureFirst, captureFirstNotification, captureFirstDuplicate);
        AssertSingleHumanDecisionNotification(expiryFirst, cancellation, expiryFirstNotification, expiryFirstDuplicate);
        Assert.Equal("Your backordered items were not available in time.", cancellation.ActionText);
    }

    [Fact]
    public void OverCeilingBackorderAndCapturedPayment_ConvergeRegardlessOfDeliveryOrder()
    {
        var captureFirst = CreateOrder();
        Assert.Null(captureFirst.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-capture-first", "checkout-1"));
        var captureFirstNotification = Assert.IsType<OrderRejected>(captureFirst.ApplyBackorderPriceChecked(false, "price-capture-first", "checkout-1"));
        var captureFirstDuplicate = captureFirst.ApplyBackorderPriceChecked(false, "price-capture-first", "checkout-1");
        Assert.Null(captureFirstDuplicate);

        var priceFirst = CreateOrder();
        var priceRejection = Assert.IsType<OrderRejected>(priceFirst.ApplyBackorderPriceChecked(false, "price-first", "checkout-1"));
        var priceFirstNotification = Assert.IsType<OrderRejected>(priceFirst.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-price-first", "checkout-1"));
        var priceFirstDuplicate = priceFirst.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-price-first", "checkout-1");
        Assert.Null(priceFirstDuplicate);

        AssertPaidUnfulfillable(captureFirst, captureFirstNotification, OrderFailureReason.PriceExceededAuthorization);
        AssertPaidUnfulfillable(priceFirst, priceFirstNotification, OrderFailureReason.PriceExceededAuthorization);
        AssertSingleHumanDecisionNotification(captureFirst, captureFirstNotification, captureFirstDuplicate);
        AssertSingleHumanDecisionNotification(priceFirst, priceRejection, priceFirstNotification, priceFirstDuplicate);
        Assert.Equal(OrderFailureReason.PriceExceededAuthorization.Name, priceRejection.FailureCategory);
    }

    [Fact]
    public void DelayedFailureAfterRetryCapture_DoesNotRegressOrNotifyAgain()
    {
        var order = CreateOrder();
        Assert.IsType<OrderPaymentActionRequired>(order.ApplyPaymentFailure("generic-decline", "Use another method.", "failure-original", "checkout-1"));
        Assert.True(order.BeginRetry("retry-1"));
        Assert.Null(order.ApplyStockReserved("stock-1", "checkout-1"));
        var confirmation = Assert.IsType<OrderConfirmed>(order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-retry", "checkout-1"));

        Assert.Null(order.ApplyPaymentFailure("generic-decline", "Use another method.", "failure-delayed", "checkout-1"));
        Assert.Null(order.ApplyPaymentCaptured(Guid.NewGuid(), 20m, "payment-retry", "checkout-1"));

        Assert.Equal(OrderStatus.Confirmed, order.Status);
        Assert.Equal(PaymentState.Captured, order.PaymentState);
        Assert.Equal(OrderFailureReason.None, order.FailureReason);
        Assert.Equal(string.Empty, order.ActionText);
        Assert.Equal(order.Id, confirmation.OrderId);
    }

    [Theory]
    [InlineData("rejected", "StockRejected")]
    [InlineData("expired", "BackorderExpired")]
    [InlineData("over-ceiling", "PriceExceededAuthorization")]
    public void TerminalStockOutcomeAndPaymentFailure_ConvergeWithoutASecondPaymentAction(string terminalPath, string expectedFailure)
    {
        var stockFirst = CreateOrder();
        var stockFirstNotification = ApplyTerminalStockOutcome(stockFirst, terminalPath, "stock-first");
        var stockFirstStatus = stockFirst.Status;
        var stockFirstPaymentState = stockFirst.PaymentState;
        var stockFirstStockState = stockFirst.StockState;
        var stockFirstFailure = stockFirst.FailureReason;
        var stockFirstAction = stockFirst.ActionText;
        var stockFirstHumanDecision = stockFirst.RequiresHumanDecision;
        var lateFailure = stockFirst.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-after-stock", "checkout-1");

        var failureFirst = CreateOrder();
        Assert.IsType<OrderPaymentActionRequired>(failureFirst.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-before-stock", "checkout-1"));
        var failureFirstNotification = ApplyTerminalStockOutcome(failureFirst, terminalPath, "stock-after-payment");

        Assert.NotNull(stockFirstNotification);
        Assert.Null(lateFailure);
        Assert.NotNull(failureFirstNotification);
        Assert.Equal(stockFirstStatus, stockFirst.Status);
        Assert.Equal(stockFirstPaymentState, stockFirst.PaymentState);
        Assert.Equal(stockFirstStockState, stockFirst.StockState);
        Assert.Equal(stockFirstFailure, stockFirst.FailureReason);
        Assert.Equal(stockFirstAction, stockFirst.ActionText);
        Assert.Equal(stockFirstHumanDecision, stockFirst.RequiresHumanDecision);
        Assert.Contains("payment-after-stock", stockFirst.ProcessedTransitionKeys, StringComparison.Ordinal);
        Assert.Equal(expectedFailure, stockFirst.FailureReason.Name);
        Assert.Equal(expectedFailure, failureFirst.FailureReason.Name);
        Assert.Equal(stockFirst.Status, failureFirst.Status);
        Assert.Equal(stockFirst.PaymentState, failureFirst.PaymentState);
        Assert.Equal(stockFirst.FailureReason, failureFirst.FailureReason);
        Assert.Equal(stockFirst.ActionText, failureFirst.ActionText);
        Assert.Equal(PaymentState.Pending, stockFirst.PaymentState);
        Assert.Null(ApplyTerminalStockOutcome(stockFirst, terminalPath, "stock-first"));
        Assert.Null(stockFirst.ApplyPaymentFailure("generic-decline", "Use another method.", "payment-after-stock", "checkout-1"));
    }

    [Theory]
    [InlineData("rejected", "StockRejected")]
    [InlineData("expired", "BackorderExpired")]
    [InlineData("over-ceiling", "PriceExceededAuthorization")]
    public async Task TerminalStockHandlers_CancelPendingPaymentsOnceAndDoNotPublishPaymentActionAfterward(string terminalPath, string expectedFailure)
    {
        var order = CreateOrder();
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);

        await ApplyTerminalStockHandlerAsync(terminalPath, order, orders, unitOfWork, bus, "stock-terminal");
        await ApplyTerminalStockHandlerAsync(terminalPath, order, orders, unitOfWork, bus, "stock-terminal");

        Assert.Equal(expectedFailure, order.FailureReason.Name);
        await bus.Received(1).PublishAsync(Arg.Is<PaymentCancellationRequestedIntegrationEvent>(evt =>
            evt.OrderId == order.Id &&
            evt.PaymentId == null &&
            evt.RequestId == "payment-cancel:stock-terminal"));

        bus.ClearReceivedCalls();
        await PaymentFailedV2Handler.Handle(CreatePaymentFailureEvent(order, "payment-after-stock"), orders, Tenant(order.TenantId), unitOfWork, bus, CancellationToken.None);

        Assert.Equal(expectedFailure, order.FailureReason.Name);
        Assert.Equal(PaymentState.Pending, order.PaymentState);
        Assert.Contains("payment-after-stock", order.ProcessedTransitionKeys, StringComparison.Ordinal);
        await bus.DidNotReceive().PublishAsync(Arg.Any<OrderPaymentActionRequiredIntegrationEvent>());

        var failureFirst = CreateOrder();
        var (failureFirstOrders, failureFirstUnitOfWork, failureFirstBus) = CreateHandlerDependencies(failureFirst);
        await PaymentFailedV2Handler.Handle(CreatePaymentFailureEvent(failureFirst, "payment-before-stock"), failureFirstOrders, Tenant(failureFirst.TenantId), failureFirstUnitOfWork, failureFirstBus, CancellationToken.None);
        await ApplyTerminalStockHandlerAsync(terminalPath, failureFirst, failureFirstOrders, failureFirstUnitOfWork, failureFirstBus, "stock-after-payment");

        Assert.Equal(order.Status, failureFirst.Status);
        Assert.Equal(order.PaymentState, failureFirst.PaymentState);
        Assert.Equal(order.FailureReason, failureFirst.FailureReason);
        Assert.Equal(order.ActionText, failureFirst.ActionText);
        await failureFirstBus.Received(1).PublishAsync(Arg.Is<PaymentCancellationRequestedIntegrationEvent>(evt =>
            evt.OrderId == failureFirst.Id &&
            evt.PaymentId == null &&
            evt.RequestId == "payment-cancel:stock-after-payment"));
    }

    [Theory]
    [InlineData("rejected", "StockRejected")]
    [InlineData("expired", "BackorderExpired")]
    [InlineData("over-ceiling", "PriceExceededAuthorization")]
    public async Task TerminalStockHandlers_KeepKnownCapturedPaymentIdOnOneCancellation(string terminalPath, string expectedFailure)
    {
        var order = CreateOrder();
        var paymentId = Guid.NewGuid();
        Assert.Null(order.ApplyPaymentCaptured(paymentId, 20m, "payment-captured", "checkout-1"));
        var (orders, unitOfWork, bus) = CreateHandlerDependencies(order);

        await ApplyTerminalStockHandlerAsync(terminalPath, order, orders, unitOfWork, bus, "stock-captured");
        await ApplyTerminalStockHandlerAsync(terminalPath, order, orders, unitOfWork, bus, "stock-captured");

        Assert.Equal(OrderStatus.PaidUnfulfillable, order.Status);
        Assert.Equal(expectedFailure, order.FailureReason.Name);
        await bus.Received(1).PublishAsync(Arg.Is<PaymentCancellationRequestedIntegrationEvent>(evt =>
            evt.OrderId == order.Id &&
            evt.PaymentId == paymentId &&
            evt.RequestId == "payment-cancel:stock-captured"));
    }

    private static void AssertPaidUnfulfillable(Order order, OrderRejected notification, OrderFailureReason expectedReason)
    {
        Assert.Equal(OrderStatus.PaidUnfulfillable, order.Status);
        Assert.True(order.RequiresHumanDecision);
        Assert.Equal(expectedReason, order.FailureReason);
        Assert.Equal(expectedReason.Name, notification.FailureCategory);
    }

    private static void AssertSingleHumanDecisionNotification(Order order, params object?[] notifications) =>
        Assert.Single(notifications.OfType<OrderRejected>().Where(notification => notification.IdempotencyKey == $"order-rejected:{order.Id:N}"));

    private static Order CreateOrder() => Order.Create(
        Guid.NewGuid(),
        "subject-1",
        Guid.NewGuid(),
        "tenant-1",
        [new OrderLine(Guid.NewGuid(), "Widget", 2, 10m)],
        25m,
        "USD",
        "checkout-1");

    private static Order CreateBackorderAwaitingPriceCheck()
    {
        var order = CreateOrder();
        Assert.Null(order.ApplyStockReserved(hasOutstandingBackorder: true, "stock-backordered", "checkout-1"));
        Assert.True(order.ApplyBackorderReady("backorder-ready", "checkout-1"));
        return order;
    }

    private static object? ApplyTerminalStockOutcome(Order order, string terminalPath, string key) => terminalPath switch
    {
        "rejected" => order.ApplyStockRejected(key, "checkout-1", "Your order could not be supplied."),
        "expired" => order.ApplyBackorderExpired(key, "checkout-1"),
        "over-ceiling" => order.ApplyBackorderPriceChecked(false, key, "checkout-1"),
        _ => throw new ArgumentOutOfRangeException(nameof(terminalPath), terminalPath, "Unsupported terminal stock path."),
    };

    private static Task ApplyTerminalStockHandlerAsync(
        string terminalPath,
        Order order,
        IGenericWriteRepository<Order, Guid> orders,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        string key) => terminalPath switch
        {
            "rejected" => StockReservationRejectedV2Handler.Handle(
                new StockReservationRejectedV2IntegrationEvent
                {
                    OrderId = order.Id,
                    SourceId = order.Id,
                    TenantId = order.TenantId,
                    IdempotencyKey = key,
                    SourceCorrelationId = "checkout-1",
                },
                orders,
                Tenant(order.TenantId),
                unitOfWork,
                bus,
                CancellationToken.None),
            "expired" => BackorderExpiredHandler.Handle(
                new BackorderExpiredIntegrationEvent
                {
                    OrderId = order.Id,
                    TenantId = order.TenantId,
                    IdempotencyKey = key,
                    SourceCorrelationId = "checkout-1",
                },
                orders,
                Tenant(order.TenantId),
                unitOfWork,
                bus,
                CancellationToken.None),
            "over-ceiling" => BackorderPriceCheckedHandler.Handle(
                CreateBackorderPriceEvent(order, order.AuthorizedAmount + 1m, order.Currency, order.AuthorizedAmount, key, isWithinAuthorizedAmount: false),
                orders,
                Tenant(order.TenantId),
                unitOfWork,
                bus,
                CancellationToken.None),
            _ => throw new ArgumentOutOfRangeException(nameof(terminalPath), terminalPath, "Unsupported terminal stock path."),
        };

    private static (IGenericWriteRepository<Order, Guid> Orders, IUnitOfWork UnitOfWork, IMessageBus Bus) CreateHandlerDependencies(Order order)
    {
        var orders = Substitute.For<IGenericWriteRepository<Order, Guid>>();
        orders.FirstOrDefaultAsync(Arg.Any<ISpecification<Order>>(), true, Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<Order?>(order));
        var unitOfWork = Substitute.For<IUnitOfWork>();
        unitOfWork.SaveChangesAsync(Arg.Any<CancellationToken>()).Returns(Task.FromResult(0));
        return (orders, unitOfWork, Substitute.For<IMessageBus>());
    }

    private static ITenantInfo Tenant(string tenantId)
    {
        var tenant = Substitute.For<ITenantInfo>();
        tenant.Id.Returns(tenantId);
        return tenant;
    }

    private static PaymentCapturedV2IntegrationEvent CreatePaymentCapturedEvent(Order order, decimal amount, string currency, decimal authorizedAmount, string requestId) =>
        new()
        {
            PaymentId = Guid.NewGuid(),
            OrderId = order.Id,
            TenantId = order.TenantId,
            Amount = amount,
            AuthorizedAmount = authorizedAmount,
            Currency = currency,
            RequestId = requestId,
            SourceCorrelationId = "checkout-1",
        };

    private static BackorderReadyIntegrationEvent CreateBackorderReadyEvent(Order order, string idempotencyKey, string sourceCorrelationId) =>
        new()
        {
            OrderId = order.Id,
            TenantId = order.TenantId,
            IdempotencyKey = idempotencyKey,
            SourceCorrelationId = sourceCorrelationId,
        };

    private static StockReservedV2IntegrationEvent CreateBackorderedReservationEvent(Order order, string idempotencyKey, string sourceCorrelationId) =>
        new()
        {
            OrderId = order.Id,
            TenantId = order.TenantId,
            IdempotencyKey = idempotencyKey,
            SourceCorrelationId = sourceCorrelationId,
            Lines = [new StockReservationLine(order.Lines[0].ProductId, order.Lines[0].Quantity, 1)],
        };

    private static PaymentFailedV2IntegrationEvent CreatePaymentFailureEvent(Order order, string requestId) =>
        new()
        {
            PaymentId = Guid.NewGuid(),
            OrderId = order.Id,
            TenantId = order.TenantId,
            Amount = order.Total,
            AuthorizedAmount = order.AuthorizedAmount,
            Currency = order.Currency,
            DeclineCategory = "generic-decline",
            ActionText = "Use another method.",
            RequestId = requestId,
            SourceCorrelationId = "checkout-1",
        };

    private static BackorderPriceCheckedIntegrationEvent CreateBackorderPriceEvent(Order order, decimal amount, string currency, decimal authorizedAmount, string requestId, bool isWithinAuthorizedAmount = true) =>
        new()
        {
            OrderId = order.Id,
            TenantId = order.TenantId,
            Amount = amount,
            AuthorizedAmount = authorizedAmount,
            Currency = currency,
            IsWithinAuthorizedAmount = isWithinAuthorizedAmount,
            RequestId = requestId,
            SourceCorrelationId = "checkout-1",
        };
}
