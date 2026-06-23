using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Logging;
using SharedKernel.Infrastructure.MultiTenant;
using Wolverine;

namespace SharedKernel.Infrastructure.Messaging.MultiTenant;

public sealed class TenantPropagationMiddleware(
    IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor,
    IMultiTenantContextSetter tenantContextSetter,
    ILogger<TenantPropagationMiddleware> logger)
{
    private const string TenantHeaderName = "X-TenantId";

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
}
