using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Logging;
using SharedKernel.Infrastructure.MultiTenant;
using Wolverine;

namespace SharedKernel.Infrastructure.Messaging.MultiTenant;

/// <summary>
/// Wolverine middleware that propagates the tenant context between incoming and outgoing messages,
/// restoring the previous tenant context once the message has been handled.
/// </summary>
/// <param name="tenantContextAccessor">Accessor used to read the current tenant context.</param>
/// <param name="tenantContextSetter">Setter used to establish the tenant context for the message scope.</param>
/// <param name="logger">The logger used to record tenant propagation activity.</param>
public sealed class TenantPropagationMiddleware(
    IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor,
    IMultiTenantContextSetter tenantContextSetter,
    ILogger<TenantPropagationMiddleware> logger)
{
    private const string TenantHeaderName = "X-TenantId";

    /// <summary>
    /// Executes before the handler, establishing the tenant context for the duration of the pipeline.
    /// If the envelope being handled carries a tenant id, that id becomes the ambient tenant; otherwise
    /// the current ambient tenant (if any) is recorded onto the envelope being handled so it is visible
    /// for the rest of this pipeline. Propagation of the tenant onto messages this handler *publishes*
    /// is handled separately by Wolverine's context tenant cascade (validated by the cross-service
    /// integration test), not by this method.
    /// Uses Wolverine's <c>Before</c>/<c>Finally</c> middleware convention: Wolverine wraps the
    /// handler in a generated <c>try/finally</c>, so the returned <see cref="TenantPropagationScope"/>
    /// is handed back to <see cref="Finally"/> to restore the prior context. Wolverine does not
    /// support an ASP.NET-style <c>next</c> continuation delegate here.
    /// </summary>
    /// <param name="context">The Wolverine message context for the current envelope.</param>
    /// <returns>A scope capturing the previous tenant context so <see cref="Finally"/> can restore it.</returns>
    public TenantPropagationScope Before(IMessageContext context)
    {
        ArgumentNullException.ThrowIfNull(context);

        string? previousTenantId = TenantPropagationContext.CurrentTenantId;
        IMultiTenantContext previousTenantContext = tenantContextAccessor.MultiTenantContext;
        var envelope = context.Envelope;
        string? envelopeTenantId = envelope?.TenantId;

        if (!string.IsNullOrWhiteSpace(envelopeTenantId))
        {
            SetTenantContext(envelopeTenantId);
        }
        else
        {
            string? currentTenantId = tenantContextAccessor.MultiTenantContext?.TenantInfo?.Id;
            if (!string.IsNullOrWhiteSpace(currentTenantId))
            {
                StampOutgoingTenant(context, currentTenantId);
                TenantPropagationContext.CurrentTenantId = currentTenantId;
            }
        }

        return new TenantPropagationScope(previousTenantId, previousTenantContext);
    }

    /// <summary>
    /// Executes after the handler (in Wolverine's generated <c>finally</c> block), restoring the
    /// tenant context captured by <see cref="Before"/>.
    /// </summary>
    /// <param name="scope">The scope returned by <see cref="Before"/>.</param>
    public void Finally(TenantPropagationScope scope)
    {
        ArgumentNullException.ThrowIfNull(scope);
        tenantContextSetter.MultiTenantContext = scope.PreviousTenantContext;
        TenantPropagationContext.CurrentTenantId = scope.PreviousTenantId;
    }

    // Despite the name, this stamps the tenant onto the envelope CURRENTLY being handled (the one on
    // context.Envelope), not onto any message this handler subsequently publishes. It records the
    // ambient tenant on the in-flight envelope so it is available for the rest of this pipeline.
    // Producer-side propagation to PUBLISHED messages happens via Wolverine's context tenant cascade
    // (exercised by the cross-service integration test), not here.
    private static void StampOutgoingTenant(IMessageContext context, string tenantId)
    {
        var envelope = context.Envelope;
        if (envelope is null)
        {
            return;
        }

        envelope.TenantId = tenantId;
        envelope.Headers[TenantHeaderName] = tenantId;
    }

    private void SetTenantContext(string tenantId)
    {
        tenantContextSetter.MultiTenantContext = new MultiTenantContext<TenantDetails>(
            new TenantDetails
            {
                Id = tenantId,
                Identifier = tenantId,
                Name = tenantId,
                IsActive = true,
            });

        TenantPropagationContext.CurrentTenantId = tenantId;
        logger.LogDebug("Propagated tenant context {TenantId} from Wolverine envelope.", tenantId);
    }

    /// <summary>
    /// Captures the tenant context that was in effect before the middleware ran, so it can be
    /// restored once the handler completes. Returned by <see cref="Before"/> and consumed by
    /// <see cref="Finally"/>.
    /// </summary>
    /// <param name="PreviousTenantId">The ambient tenant identifier prior to this message.</param>
    /// <param name="PreviousTenantContext">The Finbuckle multi-tenant context prior to this message.</param>
    public sealed record TenantPropagationScope(
        string? PreviousTenantId,
        IMultiTenantContext PreviousTenantContext);
}
