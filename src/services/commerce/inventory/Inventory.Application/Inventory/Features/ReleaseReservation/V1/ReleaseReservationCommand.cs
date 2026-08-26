namespace Inventories.Application.Inventory.Features.ReleaseReservation.V1;

/// <summary>Requests idempotent release of an order reservation and its correlated basket hold.</summary>
/// <param name="OrderId">The order whose reservation should be released.</param>
/// <param name="BasketId">The correlated basket hold, when one exists.</param>
/// <param name="TenantId">The tenant that owns both reservations.</param>
/// <param name="SourceCorrelationId">The lifecycle correlation to preserve in the outcome.</param>
/// <param name="RequestId">The stable idempotency key supplied by the requester.</param>
public sealed record ReleaseReservationCommand(
    Guid OrderId,
    Guid? BasketId,
    string TenantId,
    string SourceCorrelationId,
    string RequestId);
