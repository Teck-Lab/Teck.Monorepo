using SharedKernel.Infrastructure.MultiTenant;

namespace Gateway.Public.Edge.Steps;

/// <summary>Resolves the tenant id from token claims or header and enforces tenant/token agreement.</summary>
/// <param name="tokenContextResolver">Resolves tenant ids from a principal.</param>
/// <param name="tenantOptions">The edge tenant options.</param>
public sealed class ResolveTenantStep(
    ITenantTokenContextResolver tokenContextResolver,
    EdgeTenantOptions tenantOptions) : IEdgeStep
{
    private readonly ITenantTokenContextResolver tokenContextResolver = tokenContextResolver;
    private readonly EdgeTenantOptions tenantOptions = tenantOptions;

    /// <inheritdoc/>
    public Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct)
    {
        HttpContext http = context.HttpContext;

        if (context.Policy.Mode == EdgeAccessMode.Anonymous)
        {
            return Task.FromResult(EdgeStepResult.Proceed);
        }

        if (context.Policy.Mode == EdgeAccessMode.TenantFromHeader)
        {
            // Use the value saved by HeaderFirewallStep (before stripping) if available;
            // fall back to the raw request header when the step was not in the pipeline.
            string? headerTenant = context.ClientRequestedTenantId
                ?? TryGetHeader(http, tenantOptions.TenantIdHeaderName);

            if (string.IsNullOrWhiteSpace(headerTenant))
            {
                return Task.FromResult(EdgeStepResult.Stop(new EdgeProblem(
                    400, "Missing tenant header",
                    $"Provide '{tenantOptions.TenantIdHeaderName}' header.", "tenant.header.missing")));
            }

            return Task.FromResult(Apply(context, headerTenant));
        }

        // Authenticated: principal guaranteed by the ASP.NET auth pipeline.
        IReadOnlyList<string> tokenTenants = tokenContextResolver.ResolveTenantIds(
            http.User, tenantOptions.OrganizationClaimName, tenantOptions.TenantIdClaimName);

        if (tokenTenants.Count == 0)
        {
            return Task.FromResult(EdgeStepResult.Stop(new EdgeProblem(
                403, "Missing tenant in token",
                $"Token must contain '{tenantOptions.OrganizationClaimName}' or '{tenantOptions.TenantIdClaimName}'.",
                "tenant.token.missing")));
        }

        // Prefer the value captured by HeaderFirewallStep (before the header was stripped)
        // so we can detect mismatches even though the raw request header no longer exists.
        // Fall back to reading the raw header directly when the firewall step did not run
        // (e.g. in unit tests that call this step in isolation).
        string? effectiveHeaderTenant = context.ClientRequestedTenantId
            ?? TryGetHeader(http, tenantOptions.TenantIdHeaderName);

        if (!string.IsNullOrWhiteSpace(effectiveHeaderTenant))
        {
            if (!tokenTenants.Contains(effectiveHeaderTenant, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(EdgeStepResult.Stop(new EdgeProblem(
                    403, "Tenant mismatch",
                    $"Header '{tenantOptions.TenantIdHeaderName}' is not allowed by the token.", "tenant.mismatch")));
            }

            return Task.FromResult(Apply(context, effectiveHeaderTenant));
        }

        return Task.FromResult(Apply(context, tokenTenants[0]));
    }

    private static string? TryGetHeader(HttpContext http, string name) =>
        http.Request.Headers.TryGetValue(name, out var values) && !string.IsNullOrWhiteSpace(values.ToString())
            ? values.ToString().Trim()
            : null;

    private EdgeStepResult Apply(EdgeContext context, string tenantId)
    {
        context.ResolvedTenantId = tenantId;
        context.HttpContext.Request.Headers[tenantOptions.TenantIdHeaderName] = tenantId;
        context.HttpContext.Items[EdgeHeaders.ResolvedTenantIdItemKey] = tenantId;
        return EdgeStepResult.Proceed;
    }
}
