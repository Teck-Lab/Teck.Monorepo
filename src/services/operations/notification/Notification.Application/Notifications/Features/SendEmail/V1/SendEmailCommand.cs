namespace Notifications.Application.Notifications.Features.SendEmail.V1;

/// <summary>Requests delivery of one persisted notification.</summary>
/// <param name="DeliveryId">The identifier of the persisted delivery to send.</param>
public sealed record SendEmailCommand(Guid DeliveryId);
