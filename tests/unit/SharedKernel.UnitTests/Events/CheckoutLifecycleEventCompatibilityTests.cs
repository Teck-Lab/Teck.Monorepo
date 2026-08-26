using MemoryPack;
using SharedKernel.Core.Events;
using SharedKernel.Events;
using Xunit;

namespace SharedKernel.UnitTests.Events;

/// <summary>Protects the unchanged version-one checkout lifecycle wire contracts.</summary>
public sealed partial class CheckoutLifecycleEventCompatibilityTests
{
    /// <summary>Proves a frozen old writer's version-one bytes deserialize with the current reader.</summary>
    [Fact]
    public void V1FrozenOldWriterBytes_DeserializeWithCurrentV1Reader()
    {
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(FrozenBasket(), MemoryPackSerializer.Deserialize<BasketCheckedOutIntegrationEvent>(FrozenBasketCheckedOutPayload));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(FrozenOrder(), MemoryPackSerializer.Deserialize<OrderPlacedIntegrationEvent>(FrozenOrderPlacedPayload));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(FrozenCaptured(), MemoryPackSerializer.Deserialize<PaymentCapturedIntegrationEvent>(FrozenPaymentCapturedPayload));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(FrozenFailed(), MemoryPackSerializer.Deserialize<PaymentFailedIntegrationEvent>(FrozenPaymentFailedPayload));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(FrozenPrice(), MemoryPackSerializer.Deserialize<PriceChangedIntegrationEvent>(FrozenPriceChangedPayload));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(FrozenReserved(), MemoryPackSerializer.Deserialize<StockReservedIntegrationEvent>(FrozenStockReservedPayload));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(FrozenRejected(), MemoryPackSerializer.Deserialize<StockReservationRejectedIntegrationEvent>(FrozenStockRejectedPayload));
    }

    /// <summary>Proves current version-one bytes deserialize with the frozen old-reader mirrors.</summary>
    [Fact]
    public void CurrentV1WriterBytes_DeserializeWithFrozenOldReader()
    {
        BasketCheckedOutIntegrationEvent basket = CurrentBasket();
        OrderPlacedIntegrationEvent order = CurrentOrder();
        PaymentCapturedIntegrationEvent captured = CurrentCaptured();
        PaymentFailedIntegrationEvent failed = CurrentFailed();
        PriceChangedIntegrationEvent price = CurrentPrice();
        StockReservedIntegrationEvent reserved = CurrentReserved();
        StockReservationRejectedIntegrationEvent rejected = CurrentRejected();

        CheckoutLifecycleEventSerializationTests.AssertEquivalent(basket, MemoryPackSerializer.Deserialize<FrozenBasketCheckedOut>(MemoryPackSerializer.Serialize(basket)));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(order, MemoryPackSerializer.Deserialize<FrozenOrderPlaced>(MemoryPackSerializer.Serialize(order)));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(captured, MemoryPackSerializer.Deserialize<FrozenPaymentCaptured>(MemoryPackSerializer.Serialize(captured)));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(failed, MemoryPackSerializer.Deserialize<FrozenPaymentFailed>(MemoryPackSerializer.Serialize(failed)));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(price, MemoryPackSerializer.Deserialize<FrozenPriceChanged>(MemoryPackSerializer.Serialize(price)));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(reserved, MemoryPackSerializer.Deserialize<FrozenStockReserved>(MemoryPackSerializer.Serialize(reserved)));
        CheckoutLifecycleEventSerializationTests.AssertEquivalent(rejected, MemoryPackSerializer.Deserialize<FrozenStockRejected>(MemoryPackSerializer.Serialize(rejected)));
    }

    /// <summary>Proves V1 and V2 routing identities remain distinct.</summary>
    [Fact]
    public void V1AndV2Contracts_HaveDistinctRoutingIdentities()
    {
        Assert.NotEqual(typeof(BasketCheckedOutIntegrationEvent), typeof(BasketCheckedOutV2IntegrationEvent));
        Assert.NotEqual(typeof(OrderPlacedIntegrationEvent), typeof(OrderPlacedV2IntegrationEvent));
        Assert.NotEqual(typeof(PaymentCapturedIntegrationEvent), typeof(PaymentCapturedV2IntegrationEvent));
        Assert.NotEqual(typeof(PaymentFailedIntegrationEvent), typeof(PaymentFailedV2IntegrationEvent));
        Assert.NotEqual(typeof(PriceChangedIntegrationEvent), typeof(PriceChangedV2IntegrationEvent));
        Assert.NotEqual(typeof(StockReservedIntegrationEvent), typeof(StockReservedV2IntegrationEvent));
        Assert.NotEqual(typeof(StockReservationRejectedIntegrationEvent), typeof(StockReservationRejectedV2IntegrationEvent));
    }

    private static readonly Guid FrozenBasketId = Id(1);
    private static readonly Guid FrozenBasketCustomerId = Id(2);
    private static readonly Guid FrozenBasketProductId = Id(3);
    private static readonly Guid FrozenOrderId = Id(4);
    private static readonly Guid FrozenOrderCustomerId = Id(5);
    private static readonly Guid FrozenOrderProductId = Id(6);
    private static readonly Guid FrozenCapturedPaymentId = Id(7);
    private static readonly Guid FrozenCapturedOrderId = Id(8);
    private static readonly Guid FrozenFailedPaymentId = Id(9);
    private static readonly Guid FrozenFailedOrderId = Id(10);
    private static readonly Guid FrozenPriceProductId = Id(11);
    private static readonly Guid FrozenPriceListId = Id(12);
    private static readonly Guid FrozenReservedReservationId = Id(13);
    private static readonly Guid FrozenReservedSourceId = Id(14);
    private static readonly Guid FrozenReservedProductId = Id(15);
    private static readonly Guid FrozenRejectedReservationId = Id(16);
    private static readonly Guid FrozenRejectedSourceId = Id(17);
    private static readonly Guid FrozenRejectedProductId = Id(18);

    // These inline wire fixtures were emitted by the frozen pre-feature writer mirrors below.
    private static readonly byte[] FrozenBasketCheckedOutPayload = Convert.FromBase64String("CaFC8X+v+LxAoceVtw+p9N+E/Hj4eAHfSAAAAAAAAAAAAAAAAAAAAAAAAAABAQAAAAAAAAAAAAAAAAAAAAAAAALr////FAAAAGZyb3plbi1iYXNrZXQtdGVuYW50AAACAAAAAAA1CAAAAAAAAAAAAAAAAAAAAIR9Cvl/nwgBAAAABQAAAAAAAAAAAAAAAAAAAAPq////FQAAAGZyb3plbi1iYXNrZXQtcHJvZHVjdAAAAgAAAAAA/wgAAAAAAAAYAAAAAAACAAAAAADJCQAAAAAAAA==");
    private static readonly byte[] FrozenOrderPlacedPayload = Convert.FromBase64String("CuJHbC/B1/9IvdgVg+tvkAJehnr4eAHfSAAAAAAAAAAAAAAAAAAAAAAAAAAEAAAAAAAAAAAAAAAAAAAABez///8TAAAAZnJvemVuLW9yZGVyLXRlbmFudOz///8TAAAAZnJvemVuLW9yZGVyLXN0YXR1cwAAAgAAAAAALgoAAAAAAAAAAAAAAAAAAADiTb35f58IAQAAAAUAAAAAAAAAAAAAAAAAAAAG6////xQAAABmcm96ZW4tb3JkZXItcHJvZHVjdBwAAAAAAAIAAAAAAF0LAAAAAAAAAAACAAAAAADCCwAAAAAAAA==");
    private static readonly byte[] FrozenPaymentCapturedPayload = Convert.FromBase64String("CO3AwmtDuJ1DqZCgOEjkDZ6QxXr4eAHfSAAAAAAAAAAAAAAAAAAAAAAAAAAHAAAAAAAAAAAAAAAAAAAACOn///8WAAAAZnJvemVuLWNhcHR1cmVkLXRlbmFudAAAAgAAAAAAJwwAAAAAAAD8////AwAAAEZDUA==");
    private static readonly byte[] FrozenPaymentFailedPayload = Convert.FromBase64String("CbxWxonMF/9BlR+w0DSXixRt4nr4eAHfSAAAAAAAAAAAAAAAAAAAAAAAAAAJAAAAAAAAAAAAAAAAAAAAEOv///8UAAAAZnJvemVuLWZhaWxlZC10ZW5hbnQAAAIAAAAAAIwMAAAAAAAA/P///wMAAABGRkzr////FAAAAGZyb3plbi1mYWlsZWQtcmVhc29u");
    private static readonly byte[] FrozenPriceChangedPayload = Convert.FromBase64String("CpIdpTTfcXFDkaIhjvAQDKspAXv4eAHfSAAAAAAAAAAAAAAAAAAAAAAAAAARAAAAAAAAAAAAAAAAAAAAEuz///8TAAAAZnJvemVuLXByaWNlLXRlbmFudAAAAgAAAAAA8QwAAAAAAAD8////AwAAAEZQUgAAAAAAAAAAAMykt/p/nwjs////EwAAAGZyb3plbi1wcmljZS1jaGFuZ2U=");
    private static readonly byte[] FrozenStockReservedPayload = Convert.FromBase64String("COjT8SBkIU9Pi1qmxFO4TGLEMHv4eAHfSAAAAAAAAAAAAAAAAAAAAAAAAAAT6f///xYAAABmcm96ZW4tcmVzZXJ2ZWQtc291cmNlAAAAAAAAAAAAAAAAAAAAFOn///8WAAAAZnJvemVuLXJlc2VydmVkLXRlbmFudAEAAAADAAAAAAAAAAAAAAAAAAAAFSMAAAAkAAAA");
    private static readonly byte[] FrozenStockRejectedPayload = Convert.FromBase64String("CEd9nc2QhvNLi8xQ10dMc4u6ZXv4eAHfSAAAAAAAAAAAAAAAAAAAAAAAAAAW6f///xYAAABmcm96ZW4tcmVqZWN0ZWQtc291cmNlAAAAAAAAAAAAAAAAAAAAF+n///8WAAAAZnJvemVuLXJlamVjdGVkLXRlbmFudAEAAAADAAAAAAAAAAAAAAAAAAAAGCUAAAAmAAAA");

    private static FrozenBasketCheckedOut FrozenBasket() => new()
    {
        BasketId = FrozenBasketId, CustomerId = FrozenBasketCustomerId, TenantId = "frozen-basket-tenant", Subtotal = 21.01m,
        CheckedOutAt = At(22), Items = [new FrozenBasketLine { ProductId = FrozenBasketProductId, ProductName = "frozen-basket-product", UnitPrice = 23.03m, Quantity = 24, LineTotal = 25.05m }],
    };
    private static FrozenOrderPlaced FrozenOrder() => new()
    {
        OrderId = FrozenOrderId, CustomerId = FrozenOrderCustomerId, TenantId = "frozen-order-tenant", Status = "frozen-order-status", Total = 26.06m,
        CreatedAt = At(27), Lines = [new FrozenOrderLine { ProductId = FrozenOrderProductId, ProductName = "frozen-order-product", Quantity = 28, UnitPrice = 29.09m, Total = 30.10m }],
    };
    private static FrozenPaymentCaptured FrozenCaptured() => new() { PaymentId = FrozenCapturedPaymentId, OrderId = FrozenCapturedOrderId, TenantId = "frozen-captured-tenant", Amount = 31.11m, Currency = "FCP" };
    private static FrozenPaymentFailed FrozenFailed() => new() { PaymentId = FrozenFailedPaymentId, OrderId = FrozenFailedOrderId, TenantId = "frozen-failed-tenant", Amount = 32.12m, Currency = "FFL", Reason = "frozen-failed-reason" };
    private static FrozenPriceChanged FrozenPrice() => new() { ProductId = FrozenPriceProductId, PriceListId = FrozenPriceListId, TenantId = "frozen-price-tenant", Amount = 33.13m, Currency = "FPR", EffectiveFrom = At(34), ChangeType = "frozen-price-change" };
    private static FrozenStockReserved FrozenReserved() => new() { ReservationId = FrozenReservedReservationId, SourceType = "frozen-reserved-source", SourceId = FrozenReservedSourceId, TenantId = "frozen-reserved-tenant", Lines = [new FrozenStockLine { ProductId = FrozenReservedProductId, RequestedQuantity = 35, BackorderedQuantity = 36 }] };
    private static FrozenStockRejected FrozenRejected() => new() { ReservationId = FrozenRejectedReservationId, SourceType = "frozen-rejected-source", SourceId = FrozenRejectedSourceId, TenantId = "frozen-rejected-tenant", Lines = [new FrozenStockLine { ProductId = FrozenRejectedProductId, RequestedQuantity = 37, BackorderedQuantity = 38 }] };

    private static BasketCheckedOutIntegrationEvent CurrentBasket() => new() { BasketId = Id(101), CustomerId = Id(102), TenantId = "current-basket-tenant", Subtotal = 103.03m, CheckedOutAt = At(104), Items = [new BasketCheckedOutLine(Id(105), "current-basket-item-one", 106.06m, 107, 108.08m), new BasketCheckedOutLine(Id(109), "current-basket-item-two", 110.10m, 111, 112.12m)] };
    private static OrderPlacedIntegrationEvent CurrentOrder() => new() { OrderId = Id(113), CustomerId = Id(114), TenantId = "current-order-tenant", Status = "current-order-status", Total = 115.15m, CreatedAt = At(116), Lines = [new OrderPlacedLine(Id(117), "current-order-line-one", 118, 119.19m, 120.20m), new OrderPlacedLine(Id(121), "current-order-line-two", 122, 123.23m, 124.24m)] };
    private static PaymentCapturedIntegrationEvent CurrentCaptured() => new() { PaymentId = Id(125), OrderId = Id(126), TenantId = "current-captured-tenant", Amount = 127.27m, Currency = "CAP" };
    private static PaymentFailedIntegrationEvent CurrentFailed() => new() { PaymentId = Id(128), OrderId = Id(129), TenantId = "current-failed-tenant", Amount = 130.30m, Currency = "FLD", Reason = "current-failed-reason" };
    private static PriceChangedIntegrationEvent CurrentPrice() => new() { ProductId = Id(131), PriceListId = Id(132), TenantId = "current-price-tenant", Amount = 133.33m, Currency = "PRC", EffectiveFrom = At(134), ChangeType = "current-price-change" };
    private static StockReservedIntegrationEvent CurrentReserved() => new() { ReservationId = Id(135), SourceType = "current-reserved-source", SourceId = Id(136), TenantId = "current-reserved-tenant", Lines = [new StockReservationLine(Id(137), 138, 139), new StockReservationLine(Id(140), 141, 142)] };
    private static StockReservationRejectedIntegrationEvent CurrentRejected() => new() { ReservationId = Id(143), SourceType = "current-rejected-source", SourceId = Id(144), TenantId = "current-rejected-tenant", Lines = [new StockReservationLine(Id(145), 146, 147), new StockReservationLine(Id(148), 149, 150)] };

    private static DateTimeOffset At(int minute) => DateTimeOffset.UnixEpoch.AddMinutes(minute);

    private static Guid Id(int value) => Guid.Parse($"00000000-0000-0000-0000-{value:D12}");

    [MemoryPackable] private partial class FrozenBasketCheckedOut : IntegrationEvent { public Guid BasketId { get; set; } public Guid? CustomerId { get; set; } public string TenantId { get; set; } = string.Empty; public decimal Subtotal { get; set; } public DateTimeOffset CheckedOutAt { get; set; } public List<FrozenBasketLine> Items { get; set; } = []; }
    [MemoryPackable] private partial class FrozenBasketLine { public Guid ProductId { get; set; } public string ProductName { get; set; } = string.Empty; public decimal UnitPrice { get; set; } public int Quantity { get; set; } public decimal LineTotal { get; set; } }
    [MemoryPackable] private partial class FrozenOrderPlaced : IntegrationEvent { public Guid OrderId { get; set; } public Guid CustomerId { get; set; } public string TenantId { get; set; } = string.Empty; public string Status { get; set; } = string.Empty; public decimal Total { get; set; } public DateTimeOffset CreatedAt { get; set; } public List<FrozenOrderLine> Lines { get; set; } = []; }
    [MemoryPackable] private partial class FrozenOrderLine { public Guid ProductId { get; set; } public string ProductName { get; set; } = string.Empty; public int Quantity { get; set; } public decimal UnitPrice { get; set; } public decimal Total { get; set; } }
    [MemoryPackable] private partial class FrozenPaymentCaptured : IntegrationEvent { public Guid PaymentId { get; set; } public Guid OrderId { get; set; } public string TenantId { get; set; } = string.Empty; public decimal Amount { get; set; } public string Currency { get; set; } = string.Empty; }
    [MemoryPackable] private partial class FrozenPaymentFailed : IntegrationEvent { public Guid PaymentId { get; set; } public Guid OrderId { get; set; } public string TenantId { get; set; } = string.Empty; public decimal Amount { get; set; } public string Currency { get; set; } = string.Empty; public string Reason { get; set; } = string.Empty; }
    [MemoryPackable] private partial class FrozenPriceChanged : IntegrationEvent { public Guid ProductId { get; set; } public Guid PriceListId { get; set; } public string TenantId { get; set; } = string.Empty; public decimal Amount { get; set; } public string Currency { get; set; } = string.Empty; public DateTimeOffset EffectiveFrom { get; set; } public string ChangeType { get; set; } = string.Empty; }
    [MemoryPackable] private partial class FrozenStockReserved : IntegrationEvent { public Guid ReservationId { get; set; } public string SourceType { get; set; } = string.Empty; public Guid SourceId { get; set; } public string TenantId { get; set; } = string.Empty; public List<FrozenStockLine> Lines { get; set; } = []; }
    [MemoryPackable] private partial class FrozenStockRejected : IntegrationEvent { public Guid ReservationId { get; set; } public string SourceType { get; set; } = string.Empty; public Guid SourceId { get; set; } public string TenantId { get; set; } = string.Empty; public List<FrozenStockLine> Lines { get; set; } = []; }
    [MemoryPackable] private partial class FrozenStockLine { public Guid ProductId { get; set; } public int RequestedQuantity { get; set; } public int BackorderedQuantity { get; set; } }
}
