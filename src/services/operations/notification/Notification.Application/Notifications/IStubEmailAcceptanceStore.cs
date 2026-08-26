namespace Notifications.Application.Notifications;

/// <summary>Persists and queries durable idempotent email acceptances within the notification write context.</summary>
public interface IStubEmailAcceptanceStore
{
    /// <summary>Records a durable acceptance when the tenant/key pair has not already been accepted.</summary>
    /// <param name="message">The rendered email accepted by the stub transport.</param>
    /// <param name="tenantId">The tenant that owns the acceptance.</param>
    /// <param name="idempotencyKey">The stable key that identifies the acceptance.</param>
    /// <param name="ct">A token to observe while waiting for persistence preparation.</param>
    /// <returns>A task that completes once the acceptance is tracked by the write context.</returns>
    Task RecordAsync(EmailMessage message, string tenantId, string idempotencyKey, CancellationToken ct);

    /// <summary>Checks whether the tenant/key pair has a durable acceptance.</summary>
    /// <param name="tenantId">The tenant that owns the acceptance.</param>
    /// <param name="idempotencyKey">The stable key that identifies the acceptance.</param>
    /// <param name="ct">A token to observe while waiting for the read.</param>
    /// <returns><see langword="true" /> when the acceptance exists durably.</returns>
    Task<bool> HasAcceptedAsync(string tenantId, string idempotencyKey, CancellationToken ct);
}
