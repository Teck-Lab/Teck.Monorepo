using Ardalis.Specification;
using Notifications.Domain.Entities;

namespace Notifications.Application.Notifications.ReadModels;

/// <summary>Selects a delivery by its stable idempotency key.</summary>
public sealed class DeliveryByIdempotencyKeySpec : Specification<NotificationDelivery>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="key">The stable idempotency key to match.</param>
    public DeliveryByIdempotencyKeySpec(string key) => Query.Where(x => x.IdempotencyKey == key);
}
