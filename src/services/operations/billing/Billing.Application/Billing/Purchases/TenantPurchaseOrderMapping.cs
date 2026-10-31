using Billings.Domain.Entities;

namespace Billings.Application.Billing.Purchases;

/// <summary>Maps durable purchase orders to public DTOs.</summary>
public static class TenantPurchaseOrderMapping
{
    /// <summary>Converts a purchase order to a DTO.</summary>
    /// <param name="order">The durable purchase order.</param>
    /// <returns>The public DTO snapshot.</returns>
    public static TenantPurchaseOrderDto ToDto(TenantPurchaseOrder order) => new(
        order.Id, order.TenantId, order.TenantIdentifier, order.SubscriptionId,
        order.PlanId, order.PlanVersion, order.AmountMinor, order.Currency,
        order.DatabaseStrategy, order.DatabaseProvider, order.HasReadReplicas,
        order.ReplicaCount, order.Status, order.PaymentMethod, order.CreatedAtUtc,
        order.ConfirmedAtUtc, order.ValidUntilUtc, order.CancelledAtUtc);
}
