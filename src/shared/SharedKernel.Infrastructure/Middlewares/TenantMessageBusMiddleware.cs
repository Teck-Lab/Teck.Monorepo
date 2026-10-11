using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using SharedKernel.Infrastructure.Messaging.MultiTenant;
using SharedKernel.Infrastructure.MultiTenant;
using Wolverine;

namespace SharedKernel.Infrastructure.Middlewares;

/// <summary>
/// Seeds the request-scoped Wolverine bus from one authenticated tenant claim for the HTTP request lifetime.
/// </summary>
/// <remarks>
/// This boundary deliberately resolves tenants only from signed claims. It neither reads request headers
/// nor treats a caller-supplied tenant as authority. It establishes <c>TenantPropagationContext</c> for
/// the request so Wolverine can stamp the resolved tenant onto outgoing envelopes before durable outbox
/// persistence.
/// </remarks>
/// <param name="next">The next middleware in the HTTP pipeline.</param>
public sealed class TenantMessageBusMiddleware(RequestDelegate next)
{
    private readonly RequestDelegate next = next;

    /// <summary>
    /// Resolves one signed-claim tenant and applies it to the scoped bus for the downstream request.
    /// </summary>
    /// <param name="context">The current HTTP request context.</param>
    /// <param name="bus">The request-scoped Wolverine message bus.</param>
    /// <param name="tenantTokenContextResolver">The trusted token-claim tenant resolver.</param>
    /// <param name="tenantOptions">The configured tenant claim names.</param>
    /// <returns>A task that completes after the downstream pipeline and bus restoration.</returns>
    public async Task InvokeAsync(
        HttpContext context,
        IMessageBus bus,
        ITenantTokenContextResolver tenantTokenContextResolver,
        IOptions<TeckCloudMultiTenancyOptions> tenantOptions)
    {
        ArgumentNullException.ThrowIfNull(context);
        ArgumentNullException.ThrowIfNull(bus);
        ArgumentNullException.ThrowIfNull(tenantTokenContextResolver);
        ArgumentNullException.ThrowIfNull(tenantOptions);

        if (context.User.Identity?.IsAuthenticated != true)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        TeckCloudMultiTenancyOptions options = tenantOptions.Value;
        IReadOnlyList<string> tenantIds = tenantTokenContextResolver.ResolveTenantIds(
            context.User,
            options.OrganizationClaimName,
            options.TenantIdClaimName);

        if (tenantIds.Count != 1)
        {
            await next(context).ConfigureAwait(false);
            return;
        }

        string? previousTenantId = bus.TenantId;
        string? previousPropagationTenantId = TenantPropagationContext.CurrentTenantId;
        bus.TenantId = tenantIds[0];
        TenantPropagationContext.CurrentTenantId = tenantIds[0];

        try
        {
            await next(context).ConfigureAwait(false);
        }
        finally
        {
            bus.TenantId = previousTenantId;
            TenantPropagationContext.CurrentTenantId = previousPropagationTenantId;
        }
    }
}
