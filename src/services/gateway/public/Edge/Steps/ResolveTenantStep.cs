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

        string? headerTenant = TryGetHeader(http, tenantOptions.TenantIdHeaderName);

        if (context.Policy.Mode == EdgeAccessMode.TenantFromHeader)
        {
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

        if (!string.IsNullOrWhiteSpace(headerTenant))
        {
            if (!tokenTenants.Contains(headerTenant, StringComparer.OrdinalIgnoreCase))
            {
                return Task.FromResult(EdgeStepResult.Stop(new EdgeProblem(
                    403, "Tenant mismatch",
                    $"Header '{tenantOptions.TenantIdHeaderName}' is not allowed by the token.", "tenant.mismatch")));
            }

            return Task.FromResult(Apply(context, headerTenant));
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
