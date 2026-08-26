using System.Collections;
using System.Reflection;
using MemoryPack;
using SharedKernel.Events;
using Xunit;

namespace SharedKernel.UnitTests.Events;

/// <summary>Verifies MemoryPack round trips for the additive checkout lifecycle contracts.</summary>
public sealed partial class CheckoutLifecycleEventSerializationTests
{
    /// <summary>Documents the constructor-initialized base-member behavior of the version-one checkout wire format.</summary>
    [Fact]
    public void IntegrationEventBaseMembers_AreReinitializedByMemoryPackRoundTrip()
    {
        IntegrationEventBaseMemberProbe written = new() { IdempotencyKey = "base-member-probe-key" };

        IntegrationEventBaseMemberProbe? roundTripped = MemoryPackSerializer.Deserialize<IntegrationEventBaseMemberProbe>(MemoryPackSerializer.Serialize(written));

        Assert.NotNull(roundTripped);
        // These get-only, constructor-initialized members cannot be restored by MemoryPack; this is why checkout lifecycle contracts in this unit carry explicit IdempotencyKey values rather than relying on the base Id.
        Assert.NotEqual(written.Id, roundTripped.Id);
        Assert.NotEqual(written.CreatedOn, roundTripped.CreatedOn);
        Assert.Empty(roundTripped.MetaData);
        Assert.Equal(written.IdempotencyKey, roundTripped.IdempotencyKey);
    }

    /// <summary>Ensures every version-two lifecycle contract preserves every populated value through the configured serializer.</summary>
    [Fact]
    public void VersionTwoLifecycleContracts_RoundTripThroughMemoryPack()
    {
        AssertRoundTrips(new BasketCheckedOutLineV2 { ProductId = Id(1), ProductName = "basket-line-name", UnitPrice = 1.11m, Quantity = 2, LineTotal = 3.33m });
        AssertRoundTrips(new BasketCheckedOutV2IntegrationEvent { BasketId = Id(2), CustomerId = Id(3), KeycloakSubjectId = "basket-subject", TenantId = "basket-tenant", Amount = 4.44m, AuthorizedAmount = 5.55m, Currency = "BKT", PaymentMethodToken = "basket-token", SourceCorrelationId = "basket-correlation", CheckedOutAt = At(6), Items = [new BasketCheckedOutLineV2 { ProductId = Id(7), ProductName = "basket-item-one", UnitPrice = 8.88m, Quantity = 9, LineTotal = 10.10m }, new BasketCheckedOutLineV2 { ProductId = Id(11), ProductName = "basket-item-two", UnitPrice = 12.12m, Quantity = 13, LineTotal = 14.14m }] });
        AssertRoundTrips(new OrderPlacedV2IntegrationEvent { OrderId = Id(15), BasketId = Id(16), CustomerId = Id(17), KeycloakSubjectId = "order-subject", TenantId = "order-tenant", Amount = 18.18m, AuthorizedAmount = 19.19m, Currency = "ORD", PaymentMethodToken = "order-token", RequestId = "order-request", SourceCorrelationId = "order-correlation", CreatedAt = At(20), Lines = [new OrderPlacedLine(Id(21), "order-line-one", 22, 23.23m, 24.24m), new OrderPlacedLine(Id(25), "order-line-two", 26, 27.27m, 28.28m)] });
        AssertRoundTrips(new PaymentCapturedV2IntegrationEvent { PaymentId = Id(29), OrderId = Id(30), TenantId = "captured-tenant", Amount = 31.31m, AuthorizedAmount = 32.32m, Currency = "CAP", RequestId = "captured-request", SourceCorrelationId = "captured-correlation", CapturedAt = At(33) });
        AssertRoundTrips(new PaymentFailedV2IntegrationEvent { PaymentId = Id(34), OrderId = Id(35), TenantId = "failed-tenant", Amount = 36.36m, AuthorizedAmount = 37.37m, Currency = "FLD", DeclineCategory = "failed-category", ActionText = "failed-action", RequestId = "failed-request", SourceCorrelationId = "failed-correlation", FailedAt = At(38) });
        AssertRoundTrips(new PriceChangedV2IntegrationEvent { ProductId = Id(39), PriceListId = Id(40), TenantId = "price-tenant", Amount = 41.41m, Currency = "PRC", EffectiveFrom = At(42), ChangeType = "price-change", IdempotencyKey = "price-key" });
        AssertRoundTrips(new StockReservedV2IntegrationEvent { ReservationId = Id(43), OrderId = Id(44), BasketId = Id(45), SourceType = "reserved-source", SourceId = Id(46), SourceCorrelationId = "reserved-correlation", TenantId = "reserved-tenant", IdempotencyKey = "reserved-key", Lines = [new StockReservationLine(Id(47), 48, 49), new StockReservationLine(Id(50), 51, 52)] });
        AssertRoundTrips(new StockReservationRejectedV2IntegrationEvent { ReservationId = Id(53), OrderId = Id(54), BasketId = Id(55), SourceType = "rejected-source", SourceId = Id(56), SourceCorrelationId = "rejected-correlation", TenantId = "rejected-tenant", IdempotencyKey = "rejected-key", Lines = [new StockReservationLine(Id(57), 58, 59), new StockReservationLine(Id(60), 61, 62)] });
        AssertRoundTrips(new CatalogPriceChangedIntegrationEvent { ProductId = Id(63), VariantId = Id(64), TenantId = "catalog-change-tenant", Amount = 65.65m, Currency = "CPC", IdempotencyKey = "catalog-change-key", ChangedAt = At(66) });
        AssertRoundTrips(new CatalogPriceReconciliationRequestedIntegrationEvent { ProductId = Id(67), TenantId = "catalog-request-tenant", RequestId = "catalog-request-id", SourceCorrelationId = "catalog-request-correlation" });
        AssertRoundTrips(new CatalogPriceReconciledIntegrationEvent { ProductId = Id(68), VariantId = Id(69), TenantId = "catalog-reconciled-tenant", Amount = 70.70m, Currency = "CPR", RequestId = "catalog-reconciled-request", SourceCorrelationId = "catalog-reconciled-correlation" });
        AssertRoundTrips(new BasketCheckoutRequestedLine { ProductId = Id(71), Quantity = 72 });
        AssertRoundTrips(new BasketCheckoutRequestedIntegrationEvent { BasketId = Id(73), TenantId = "checkout-request-tenant", AuthorizedAmount = 74.74m, Currency = "BCR", RequestId = "checkout-request-id", SourceCorrelationId = "checkout-request-correlation", Lines = [new BasketCheckoutRequestedLine { ProductId = Id(75), Quantity = 76 }, new BasketCheckoutRequestedLine { ProductId = Id(77), Quantity = 78 }] });
        AssertRoundTrips(new BasketPricedLine { ProductId = Id(79), UnitPrice = 80.80m, Quantity = 81, LineTotal = 82.82m });
        AssertRoundTrips(new BasketPricedIntegrationEvent { BasketId = Id(83), TenantId = "priced-tenant", Amount = 84.84m, AuthorizedAmount = 85.85m, Currency = "BPR", RequestId = "priced-request", SourceCorrelationId = "priced-correlation", Lines = [new BasketPricedLine { ProductId = Id(86), UnitPrice = 87.87m, Quantity = 88, LineTotal = 89.89m }, new BasketPricedLine { ProductId = Id(90), UnitPrice = 91.91m, Quantity = 92, LineTotal = 93.93m }] });
        AssertRoundTrips(new BasketPricingFailedIntegrationEvent { BasketId = Id(94), TenantId = "pricing-failed-tenant", RequestId = "pricing-failed-request", SourceCorrelationId = "pricing-failed-correlation", FailureCategory = "pricing-failed-category", ActionText = "pricing-failed-action" });
        AssertRoundTrips(new PaymentRetryRequestedIntegrationEvent { OrderId = Id(95), TenantId = "retry-tenant", AuthorizedAmount = 96.96m, Currency = "RET", PaymentMethodToken = "retry-token", RequestId = "retry-request", SourceCorrelationId = "retry-correlation" });
        AssertRoundTrips(new PaymentCancellationRequestedIntegrationEvent { OrderId = Id(97), PaymentId = Id(98), TenantId = "cancel-tenant", RequestId = "cancel-request", SourceCorrelationId = "cancel-correlation" });
        AssertRoundTrips(new StockReleaseRequestedIntegrationEvent { OrderId = Id(99), BasketId = Id(100), TenantId = "release-request-tenant", SourceCorrelationId = "release-request-correlation", RequestId = "release-request-id" });
        AssertRoundTrips(new StockReleasedIntegrationEvent { OrderId = Id(101), BasketId = Id(102), TenantId = "released-tenant", SourceCorrelationId = "released-correlation", RequestId = "released-request" });
        AssertRoundTrips(new BackorderReadyIntegrationEvent { OrderId = Id(103), BasketId = Id(104), TenantId = "ready-tenant", SourceCorrelationId = "ready-correlation", IdempotencyKey = "ready-key", ReadyAt = At(105) });
        AssertRoundTrips(new BackorderExpiredIntegrationEvent { OrderId = Id(106), BasketId = Id(107), TenantId = "expired-tenant", SourceCorrelationId = "expired-correlation", IdempotencyKey = "expired-key", ExpiredAt = At(108) });
        AssertRoundTrips(new BackorderPriceCheckRequestedIntegrationEvent { OrderId = Id(109), BasketId = Id(110), TenantId = "backorder-check-tenant", AuthorizedAmount = 111.11m, Currency = "BPC", SourceCorrelationId = "backorder-check-correlation", RequestId = "backorder-check-request", Lines = [new OrderPlacedLine(Id(112), "backorder-line-one", 113, 114.14m, 115.15m), new OrderPlacedLine(Id(116), "backorder-line-two", 117, 118.18m, 119.19m)] });
        AssertRoundTrips(new BackorderPriceCheckedIntegrationEvent { OrderId = Id(120), BasketId = Id(121), TenantId = "backorder-checked-tenant", Amount = 122.22m, AuthorizedAmount = 123.23m, Currency = "BPD", IsWithinAuthorizedAmount = true, FailureCategory = "backorder-checked-category", SourceCorrelationId = "backorder-checked-correlation", RequestId = "backorder-checked-request" });
        AssertRoundTrips(new CustomerContactReconciliationRequestedIntegrationEvent { CustomerId = Id(124), KeycloakSubjectId = "contact-request-subject", TenantId = "contact-request-tenant", RequestId = "contact-request-id", SourceCorrelationId = "contact-request-correlation" });
        AssertRoundTrips(new CustomerContactReconciledIntegrationEvent { CustomerId = Id(125), KeycloakSubjectId = "contact-reconciled-subject", TenantId = "contact-reconciled-tenant", Email = "contact@example.test", RequestId = "contact-reconciled-id", SourceCorrelationId = "contact-reconciled-correlation" });
        AssertRoundTrips(new OrderConfirmedIntegrationEvent { OrderId = Id(126), CustomerId = Id(127), KeycloakSubjectId = "confirmed-subject", TenantId = "confirmed-tenant", Amount = 128.28m, Currency = "CNF", IdempotencyKey = "confirmed-key", SourceCorrelationId = "confirmed-correlation", AuthorizedAmount = 129.29m });
        AssertRoundTrips(new OrderPaymentActionRequiredIntegrationEvent { OrderId = Id(130), CustomerId = Id(131), KeycloakSubjectId = "action-subject", TenantId = "action-tenant", DeclineCategory = "action-category", ActionText = "action-text", IdempotencyKey = "action-key", SourceCorrelationId = "action-correlation" });
        AssertRoundTrips(new OrderCancelledIntegrationEvent { OrderId = Id(132), CustomerId = Id(133), KeycloakSubjectId = "cancelled-subject", TenantId = "cancelled-tenant", ActionText = "cancelled-action", IdempotencyKey = "cancelled-key", SourceCorrelationId = "cancelled-correlation" });
        AssertRoundTrips(new OrderRejectedIntegrationEvent { OrderId = Id(134), CustomerId = Id(135), KeycloakSubjectId = "rejected-subject", TenantId = "rejected-tenant", FailureCategory = "rejected-category", ActionText = "rejected-action", IdempotencyKey = "rejected-key", SourceCorrelationId = "rejected-correlation" });
        AssertRoundTrips(new OrderBackorderOutcomeIntegrationEvent { OrderId = Id(136), CustomerId = Id(137), KeycloakSubjectId = "outcome-subject", TenantId = "outcome-tenant", Outcome = "outcome-value", ActionText = "outcome-action", IdempotencyKey = "outcome-key", SourceCorrelationId = "outcome-correlation" });
    }

    internal static void AssertEquivalent(object expected, object? actual, string path = "message")
    {
        Assert.NotNull(actual);

        if (expected is string or Guid or DateTimeOffset or DateTime or decimal or int or bool)
        {
            Assert.Equal(expected, actual);
            return;
        }

        if (expected is IEnumerable expectedItems)
        {
            Assert.IsAssignableFrom<IEnumerable>(actual);
            object?[] expectedValues = expectedItems.Cast<object?>().ToArray();
            object?[] actualValues = ((IEnumerable)actual).Cast<object?>().ToArray();
            Assert.Equal(expectedValues.Length, actualValues.Length);
            for (int index = 0; index < expectedValues.Length; index++)
            {
                AssertEquivalent(expectedValues[index]!, actualValues[index], $"{path}[{index}]");
            }

            return;
        }

        PropertyInfo[] expectedProperties = expected.GetType().GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        Assert.NotEmpty(expectedProperties);
        foreach (PropertyInfo expectedProperty in expectedProperties)
        {
            PropertyInfo? actualProperty = actual!.GetType().GetProperty(expectedProperty.Name, BindingFlags.Instance | BindingFlags.Public);
            Assert.NotNull(actualProperty);
            object? expectedValue = expectedProperty.GetValue(expected);
            Assert.NotNull(expectedValue);
            AssertEquivalent(expectedValue, actualProperty!.GetValue(actual), $"{path}.{expectedProperty.Name}");
        }
    }

    private static void AssertRoundTrips<T>(T message)
        where T : class
    {
        T? roundTripped = MemoryPackSerializer.Deserialize<T>(MemoryPackSerializer.Serialize(message));

        AssertEquivalent(message, roundTripped);
    }

    private static DateTimeOffset At(int minute) => DateTimeOffset.UnixEpoch.AddMinutes(minute);

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

    [MemoryPackable]
    private partial class IntegrationEventBaseMemberProbe : SharedKernel.Core.Events.IntegrationEvent
    {
        public string IdempotencyKey { get; set; } = string.Empty;
    }
}
