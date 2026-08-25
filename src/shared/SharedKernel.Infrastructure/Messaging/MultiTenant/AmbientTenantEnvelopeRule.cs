using Wolverine;

namespace SharedKernel.Infrastructure.Messaging.MultiTenant;

/// <summary>
/// Stamps the tenant already resolved for the current asynchronous flow onto outgoing Wolverine envelopes.
/// </summary>
/// <remarks>
/// The tenant value is established by <see cref="TenantPropagationMiddleware"/> from the authenticated
/// multi-tenant context. This rule deliberately does not inspect HTTP headers or message payloads, so an
/// untrusted caller cannot choose an envelope tenant.
/// </remarks>
internal sealed class AmbientTenantEnvelopeRule : IEnvelopeRule
{
    internal const string TenantHeaderName = "X-TenantId";

    /// <inheritdoc />
    public void Modify(Envelope envelope)
    {
        ArgumentNullException.ThrowIfNull(envelope);

        if (!string.IsNullOrWhiteSpace(envelope.TenantId))
        {
            envelope.Headers[TenantHeaderName] = envelope.TenantId;
            return;
        }

        string? tenantId = TenantPropagationContext.CurrentTenantId;
        if (string.IsNullOrWhiteSpace(tenantId))
        {
            return;
        }

        envelope.TenantId = tenantId;
        envelope.Headers[TenantHeaderName] = tenantId;
    }
}
