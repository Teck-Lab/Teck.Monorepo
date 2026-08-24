using Ardalis.Specification;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;

namespace Notifications.Application.Notifications.ReadModels;

/// <summary>Selects pending deliveries waiting on one contact reconciliation request.</summary>
public sealed class PendingDeliveriesByContactRequestSpec : Specification<NotificationDelivery>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="requestId">The contact reconciliation request identifier to match.</param>
    public PendingDeliveriesByContactRequestSpec(string requestId) => Query.Where(x => x.ContactRequestId == requestId && x.Status == DeliveryStatus.Pending);
}
