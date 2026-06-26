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
    /// Invokes the middleware, establishing the tenant context from the incoming envelope (or stamping
    /// the current tenant onto the outgoing envelope) for the duration of the pipeline.
    /// </summary>
    /// <param name="context">The Wolverine message context for the current envelope.</param>
    /// <param name="next">The delegate that continues the message handling pipeline.</param>
    /// <param name="cancellationToken">A token to observe while waiting for the operation to complete.</param>
    /// <returns>A <see cref="ValueTask"/> representing the asynchronous operation.</returns>
    public async ValueTask InvokeAsync(
        IMessageContext context,
        Func<ValueTask> next,
        CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(next);

        string? previousTenantId = TenantPropagationContext.CurrentTenantId;
        IMultiTenantContext? previousTenantContext = tenantContextAccessor.MultiTenantContext;
        var envelope = context.Envelope;
        string? envelopeTenantId = envelope?.TenantId;

        try
        {
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

            await next().ConfigureAwait(false);
        }
        finally
        {
            tenantContextSetter.MultiTenantContext = previousTenantContext;
            TenantPropagationContext.CurrentTenantId = previousTenantId;
        }
    }

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
}
