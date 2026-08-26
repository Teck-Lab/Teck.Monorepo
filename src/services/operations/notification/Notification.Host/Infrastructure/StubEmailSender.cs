using Notifications.Application.Notifications;

namespace Notifications.Host.Infrastructure;

/// <summary>Durable deterministic email transport used by notification tests and local hosts.</summary>
/// <param name="acceptances">The application-owned durable acceptance boundary.</param>
public sealed class StubEmailSender(IStubEmailAcceptanceStore acceptances) : IEmailSender
{
    /// <inheritdoc />
    public Task SendAsync(EmailMessage message, string tenantId, string idempotencyKey, CancellationToken ct) =>
        acceptances.RecordAsync(message, tenantId, idempotencyKey, ct);

    /// <inheritdoc />
    public Task<bool> HasAcceptedAsync(string tenantId, string idempotencyKey, CancellationToken ct) =>
        acceptances.HasAcceptedAsync(tenantId, idempotencyKey, ct);
}
