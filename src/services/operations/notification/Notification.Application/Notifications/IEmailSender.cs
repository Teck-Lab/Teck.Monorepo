namespace Notifications.Application.Notifications;

/// <summary>Abstraction for the sole notification transport used by this capability.</summary>
public interface IEmailSender
{
    /// <summary>Sends a rendered message with its stable idempotency key.</summary>
    /// <param name="message">The fully rendered message to send.</param>
    /// <param name="tenantId">The tenant owning the delivery.</param>
    /// <param name="idempotencyKey">The stable key used to prevent duplicate delivery.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes once the sender accepts or rejects the message.</returns>
    Task SendAsync(EmailMessage message, string tenantId, string idempotencyKey, CancellationToken ct);

    /// <summary>Checks whether the sender durably accepted the stable delivery key.</summary>
    /// <param name="tenantId">The tenant owning the delivery.</param>
    /// <param name="idempotencyKey">The stable key used to prevent duplicate delivery.</param>
    /// <param name="ct">A token to observe while waiting for the read.</param>
    /// <returns><see langword="true" /> when a durable acceptance exists.</returns>
    Task<bool> HasAcceptedAsync(string tenantId, string idempotencyKey, CancellationToken ct);
}

/// <summary>Represents a fully rendered email delivered through <see cref="IEmailSender"/>.</summary>
/// <param name="Recipient">The destination email address.</param>
/// <param name="Subject">The rendered email subject.</param>
/// <param name="Body">The rendered email body.</param>
public sealed record EmailMessage(string Recipient, string Subject, string Body);
