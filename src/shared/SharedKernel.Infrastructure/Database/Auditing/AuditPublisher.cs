using SharedKernel.Events;
using Wolverine;

namespace SharedKernel.Infrastructure.Database.Auditing;

/// <summary>
/// Publishes audit events to the message bus.
/// </summary>
/// <param name="messageBus">The message bus used to publish audit events.</param>
public sealed class AuditPublisher(IMessageBus messageBus)
{
    private readonly IMessageBus _messageBus = messageBus;

    /// <summary>
    /// Publishes the specified audit event to the message bus.
    /// </summary>
    /// <param name="auditEvent">The audit event to publish.</param>
    /// <param name="deliveryOptions">The optional delivery options for the message.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous publish operation.</returns>
    public ValueTask PublishAsync(AuditEvent auditEvent, DeliveryOptions? deliveryOptions = null)
    {
        return _messageBus.PublishAsync(auditEvent, deliveryOptions ?? new DeliveryOptions());
    }
}
