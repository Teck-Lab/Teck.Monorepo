using SharedKernel.Core.Events;

namespace Order.Host.Infrastructure.IntegrationEvents;

public sealed record OrderPlacedIntegrationEvent(
    Guid OrderId,
    Guid CustomerId,
    string TenantId,
    decimal Total,
    DateTimeOffset Timestamp) : IIntegrationEvent;
