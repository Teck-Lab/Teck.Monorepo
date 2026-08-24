using Notifications.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Notifications.Domain.Entities;

/// <summary>Durable, idempotent record of one shopper notification.</summary>
public sealed class NotificationDelivery : BaseEntity, IAggregateRoot, ITenantScoped
{
    private NotificationDelivery() { }
    /// <inheritdoc />
    public string TenantId { get; set; } = string.Empty;
    /// <summary>Gets the optional customer identifier.</summary>
    public Guid? CustomerId { get; private set; }
    /// <summary>Gets the source order identifier.</summary>
    public Guid OrderId { get; private set; }
    /// <summary>Gets the immutable customer subject.</summary>
    public string KeycloakSubjectId { get; private set; } = string.Empty;
    /// <summary>Gets the stable idempotency key.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;
    /// <summary>Gets the source event correlation identifier.</summary>
    public string SourceCorrelationId { get; private set; } = string.Empty;
    /// <summary>Gets the reconciliation request key when contact information was initially absent.</summary>
    public string? ContactRequestId { get; private set; }
    /// <summary>Gets the selected fixed template kind.</summary>
    public NotificationKind Kind { get; private set; } = NotificationKind.OrderConfirmed;
    /// <summary>Gets the resolved recipient email, if contact information is available.</summary>
    public string? Recipient { get; private set; }
    /// <summary>Gets the rendered email subject.</summary>
    public string Subject { get; private set; } = string.Empty;
    /// <summary>Gets the rendered email body.</summary>
    public string Body { get; private set; } = string.Empty;
    /// <summary>Gets the durable delivery status.</summary>
    public DeliveryStatus Status { get; private set; } = DeliveryStatus.Pending;
    /// <summary>Gets when delivery was accepted by the sender.</summary>
    public DateTimeOffset? SentAt { get; private set; }
    /// <summary>Creates a pending delivery.</summary>
    /// <param name="tenantId">The owning tenant.</param>
    /// <param name="customerId">The optional customer identifier.</param>
    /// <param name="orderId">The source order identifier.</param>
    /// <param name="subject">The immutable customer subject.</param>
    /// <param name="idempotencyKey">The stable delivery key.</param>
    /// <param name="sourceCorrelationId">The source correlation identifier.</param>
    /// <param name="kind">The fixed template kind.</param>
    /// <param name="emailSubject">The rendered subject.</param>
    /// <param name="body">The rendered body.</param>
    /// <param name="recipient">The resolved recipient, if present.</param>
    /// <param name="contactRequestId">The pending contact request key, if present.</param>
    /// <returns>The new pending delivery.</returns>
    public static NotificationDelivery Create(string tenantId, Guid? customerId, Guid orderId, string subject, string idempotencyKey, string sourceCorrelationId, NotificationKind kind, string emailSubject, string body, string? recipient, string? contactRequestId) => new() { TenantId = tenantId, CustomerId = customerId, OrderId = orderId, KeycloakSubjectId = subject, IdempotencyKey = idempotencyKey, SourceCorrelationId = sourceCorrelationId, Kind = kind, Subject = emailSubject, Body = body, Recipient = recipient, ContactRequestId = contactRequestId };
    /// <summary>Resolves a previously pending delivery to a contact.</summary>
    /// <param name="recipient">The resolved recipient email.</param>
    public void ResolveRecipient(string recipient) => Recipient = recipient;
    /// <summary>Records successful dispatch.</summary>
    /// <param name="now">The dispatch timestamp.</param>
    public void MarkSent(DateTimeOffset now) { Status = DeliveryStatus.Sent; SentAt = now; }
    /// <summary>Records a retryable transport failure.</summary>
    public void MarkRetryable() => Status = DeliveryStatus.Retryable;
}
