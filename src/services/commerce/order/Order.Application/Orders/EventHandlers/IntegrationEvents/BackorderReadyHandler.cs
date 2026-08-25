using Finbuckle.MultiTenant.Abstractions;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Requests authoritative repricing when a backorder becomes stock-ready.</summary>
public static class BackorderReadyHandler
{
    /// <summary>Moves the order to price-check pending and publishes exactly one request.</summary>
    /// <param name="evt">The backorder-ready outcome.</param>
    /// <param name="orders">The tracked order repository.</param>
    /// <param name="tenant">The tenant established from the Wolverine envelope.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after a possible price-check publication.</returns>
    public static async Task Handle(BackorderReadyIntegrationEvent evt, IGenericWriteRepository<Order, Guid> orders, ITenantInfo tenant, IUnitOfWork unitOfWork, IMessageBus bus, CancellationToken ct)
    {
        OrderEventTenantGuard.EnsureMatchesEnvelope(evt.TenantId, tenant);

        var order = await orders.FirstOrDefaultAsync(new ReadModels.OrderByIdSpec(evt.OrderId, tenant.Id), enableTracking: true, ct).ConfigureAwait(false);
        if (order is null || !string.Equals(order.TenantId, tenant.Id, StringComparison.Ordinal) || !order.ApplyBackorderReady(evt.IdempotencyKey, evt.SourceCorrelationId))
        {
            return;
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        if (order.StockState != StockState.AwaitingPriceCheck)
        {
            return;
        }

        await PublishPriceCheckAsync(order, evt.IdempotencyKey, evt.SourceCorrelationId, bus).ConfigureAwait(false);
    }

    /// <summary>Publishes the one authoritative price-check request for an accepted ready fact.</summary>
    /// <param name="order">The tracked order awaiting repricing.</param>
    /// <param name="idempotencyKey">The original ready idempotency key.</param>
    /// <param name="sourceCorrelationId">The original ready source correlation.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <returns>A task that completes after publication.</returns>
    public static ValueTask PublishPriceCheckAsync(Order order, string idempotencyKey, string sourceCorrelationId, IMessageBus bus) =>
        bus.PublishAsync(new BackorderPriceCheckRequestedIntegrationEvent
        {
            OrderId = order.Id,
            BasketId = order.BasketId,
            TenantId = order.TenantId,
            AuthorizedAmount = order.AuthorizedAmount,
            Currency = order.Currency,
            SourceCorrelationId = sourceCorrelationId,
            RequestId = $"backorder-price:{idempotencyKey}",
            Lines = order.Lines.Select(line => new OrderPlacedLine(line.ProductId, line.ProductName, line.Quantity, line.UnitPrice, line.Total)).ToList(),
        });
}
