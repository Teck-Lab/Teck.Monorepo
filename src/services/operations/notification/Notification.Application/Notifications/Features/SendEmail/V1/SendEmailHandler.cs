using Microsoft.EntityFrameworkCore;
using Notifications.Application.Notifications.ReadModels;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;
using Npgsql;
using SharedKernel.Core.Database;

namespace Notifications.Application.Notifications.Features.SendEmail.V1;

/// <summary>Dispatches a persisted notification through the configured email boundary.</summary>
public static class SendEmailHandler
{
    private const string StubEmailAcceptanceUniqueIndex = "IX_stub_email_acceptances_TenantId_IdempotencyKey";

    /// <summary>Sends a pending delivery once and records its terminal transport result.</summary>
    /// <param name="command">The request identifying the delivery.</param>
    /// <param name="deliveries">The delivery write repository.</param>
    /// <param name="unitOfWork">The unit of work used to persist the result.</param>
    /// <param name="sender">The configured email sender.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after recording the delivery result.</returns>
    public static async Task Handle(SendEmailCommand command, IGenericWriteRepository<NotificationDelivery, Guid> deliveries, IUnitOfWork unitOfWork, IEmailSender sender, CancellationToken ct)
    {
        var delivery = await deliveries.FirstOrDefaultAsync(new DeliveryByIdSpec(command.DeliveryId), enableTracking: true, ct).ConfigureAwait(false);
        if (delivery is null || delivery.Status == DeliveryStatus.Sent || string.IsNullOrWhiteSpace(delivery.Recipient))
        {
            return;
        }

        try
        {
            await unitOfWork.BeginTransactionAsync(cancellationToken: ct).ConfigureAwait(false);
            await sender.SendAsync(new EmailMessage(delivery.Recipient, delivery.Subject, delivery.Body), delivery.TenantId, delivery.IdempotencyKey, ct).ConfigureAwait(false);
            delivery.MarkSent(DateTimeOffset.UtcNow);
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            await unitOfWork.CommitTransactionAsync(ct).ConfigureAwait(false);
        }
        catch (DbUpdateException exception) when (IsStubEmailAcceptanceRace(exception))
        {
            await unitOfWork.RollbackTransactionAsync(ct).ConfigureAwait(false);
            if (await HasDurableWinnerAsync(command, delivery, deliveries, sender, ct).ConfigureAwait(false))
            {
                return;
            }

            delivery.MarkRetryable();
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            throw;
        }
        catch
        {
            await unitOfWork.RollbackTransactionAsync(ct).ConfigureAwait(false);
            delivery.MarkRetryable();
            await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
            throw;
        }
    }

    private static bool IsStubEmailAcceptanceRace(DbUpdateException exception) =>
        exception.InnerException is PostgresException
        {
            SqlState: PostgresErrorCodes.UniqueViolation,
            ConstraintName: StubEmailAcceptanceUniqueIndex,
        };

    private static async Task<bool> HasDurableWinnerAsync(SendEmailCommand command, NotificationDelivery delivery, IGenericWriteRepository<NotificationDelivery, Guid> deliveries, IEmailSender sender, CancellationToken ct)
    {
        var acceptanceExists = await sender.HasAcceptedAsync(delivery.TenantId, delivery.IdempotencyKey, ct).ConfigureAwait(false);
        var persistedDelivery = await deliveries.FirstOrDefaultAsync(new DeliveryByIdSpec(command.DeliveryId), enableTracking: false, ct).ConfigureAwait(false);
        return acceptanceExists && persistedDelivery?.Status == DeliveryStatus.Sent;
    }
}
