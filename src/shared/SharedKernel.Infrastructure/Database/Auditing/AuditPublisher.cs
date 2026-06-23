using SharedKernel.Events;
using Wolverine;

namespace SharedKernel.Infrastructure.Database.Auditing;

public sealed class AuditPublisher(IMessageBus messageBus)
{
    private readonly IMessageBus _messageBus = messageBus;

    public ValueTask PublishAsync(AuditEvent auditEvent, DeliveryOptions? deliveryOptions = null)
    {
        return _messageBus.PublishAsync(auditEvent, deliveryOptions ?? new DeliveryOptions());
    }
}
