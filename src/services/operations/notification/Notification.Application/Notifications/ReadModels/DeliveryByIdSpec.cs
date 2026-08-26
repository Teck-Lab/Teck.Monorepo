using Ardalis.Specification;
using Notifications.Domain.Entities;

namespace Notifications.Application.Notifications.ReadModels;

/// <summary>Selects a delivery by identifier.</summary>
public sealed class DeliveryByIdSpec : Specification<NotificationDelivery>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="id">The delivery identifier to match.</param>
    public DeliveryByIdSpec(Guid id) => Query.Where(x => x.Id == id);
}
