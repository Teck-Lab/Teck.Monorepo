using Orders.Application.Orders.Mapping;
using Orders.Application.Orders.ReadModels;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;

namespace Orders.Application.Orders.Features.CreateOrder.V1;

/// <summary>Persists an idempotent order from an authoritative checkout and publishes V2 placement.</summary>
public static class CreateOrderHandler
{
    /// <summary>Creates the order once for a stable checkout correlation.</summary>
    /// <param name="command">The authoritative checkout command.</param>
    /// <param name="repository">The tracked order repository.</param>
    /// <param name="unitOfWork">The single commit boundary.</param>
    /// <param name="bus">The Wolverine message bus.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>The created or previously persisted order.</returns>
    public static async Task<OrderDto> Handle(
        CreateOrderCommand command,
        IGenericWriteRepository<Order, Guid> repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var existing = await repository.FirstOrDefaultAsync(new OrderByCheckoutCorrelationSpec(command.SourceCorrelationId, command.TenantId), enableTracking: true, ct).ConfigureAwait(false);
        if (existing is not null)
        {
            return existing.ToDto();
        }

        var order = Order.Create(
            command.CustomerId,
            command.KeycloakSubjectId,
            command.BasketId,
            command.TenantId,
            command.ToEntity().Lines,
            command.AuthorizedAmount,
            command.Currency,
            command.SourceCorrelationId);
        await repository.AddAsync(order, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new OrderPlacedV2IntegrationEvent
        {
            OrderId = order.Id,
            BasketId = order.BasketId,
            CustomerId = order.CustomerId,
            KeycloakSubjectId = order.KeycloakSubjectId,
            TenantId = order.TenantId,
            Amount = order.Total,
            AuthorizedAmount = order.AuthorizedAmount,
            Currency = order.Currency,
            PaymentMethodToken = command.PaymentMethodToken,
            RequestId = command.SourceCorrelationId,
            SourceCorrelationId = command.SourceCorrelationId,
            CreatedAt = order.CreatedAt,
            Lines = order.Lines.Select(line => new OrderPlacedLine(line.ProductId, line.ProductName, line.Quantity, line.UnitPrice, line.Total)).ToList(),
        }).ConfigureAwait(false);
        return order.ToDto();
    }
}
