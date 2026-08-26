using SharedKernel.Core.Domain;

namespace Notifications.Domain.Entities;

/// <summary>Durable receipt for a deterministic stub transport acceptance.</summary>
public sealed class StubEmailAcceptance : BaseEntity, IAggregateRoot, ITenantScoped
{
    private StubEmailAcceptance() { }

    /// <inheritdoc />
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the stable delivery key accepted by the stub transport.</summary>
    public string IdempotencyKey { get; private set; } = string.Empty;

    /// <summary>Gets the accepted recipient.</summary>
    public string Recipient { get; private set; } = string.Empty;

    /// <summary>Gets the accepted subject.</summary>
    public string Subject { get; private set; } = string.Empty;

    /// <summary>Gets the accepted message body.</summary>
    public string Body { get; private set; } = string.Empty;

    /// <summary>Creates a durable stub acceptance.</summary>
    /// <param name="tenantId">The tenant owning the delivery.</param>
    /// <param name="idempotencyKey">The stable delivery key.</param>
    /// <param name="recipient">The accepted recipient.</param>
    /// <param name="subject">The accepted subject.</param>
    /// <param name="body">The accepted message body.</param>
    /// <returns>The durable stub acceptance.</returns>
    public static StubEmailAcceptance Create(string tenantId, string idempotencyKey, string recipient, string subject, string body) => new()
    {
        TenantId = tenantId,
        IdempotencyKey = idempotencyKey,
        Recipient = recipient,
        Subject = subject,
        Body = body,
    };
}
