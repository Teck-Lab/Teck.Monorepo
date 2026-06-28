using Orders.Application.Orders.IntegrationEvents;
using Orders.Application.Orders.Mapping;
using Orders.Application.Orders.Responses;
using Orders.Domain.Entities;
using SharedKernel.Core.Database;
using Wolverine;

namespace Orders.Application.Orders.Features.CreateOrder.V1;

/// <summary>
/// Handles the <see cref="CreateOrderCommand"/> by persisting a new order and publishing its placed event.
/// </summary>
public static class CreateOrderHandler
{
    /// <summary>
    /// Creates and persists an order, then publishes an <see cref="OrderPlacedIntegrationEvent"/>.
    /// </summary>
    /// <param name="command">The command describing the order to create.</param>
    /// <param name="repository">The write repository used to persist the order.</param>
    /// <param name="unitOfWork">The unit of work used to commit changes.</param>
    /// <param name="bus">The message bus used to publish the integration event.</param>
    /// <param name="ct">A token used to cancel the operation.</param>
    /// <returns>The created order represented as an <see cref="OrderDto"/>.</returns>
    public static async Task<OrderDto> Handle(
        CreateOrderCommand command,
        IGenericWriteRepository<Order, Guid> repository,
        IUnitOfWork unitOfWork,
        IMessageBus bus,
        CancellationToken ct)
    {
        var (customerId, tenantId, lines) = OrderMapper.ToEntity(command);
        var order = Order.Create(customerId, tenantId, lines);

        await repository.AddAsync(order, ct).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);

        await bus.PublishAsync(new OrderPlacedIntegrationEvent(order)).ConfigureAwait(false);

        return OrderMapper.ToDto(order);
    }
}
