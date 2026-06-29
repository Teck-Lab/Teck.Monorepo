namespace Gateway.Public.Edge.Steps;

/// <summary>Strips client-supplied trusted internal headers so only the gateway can set them.</summary>
/// <param name="tenantOptions">The edge tenant options.</param>
public sealed class HeaderFirewallStep(EdgeTenantOptions tenantOptions) : IEdgeStep
{
    private readonly EdgeTenantOptions tenantOptions = tenantOptions;

    /// <inheritdoc/>
    public Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct)
    {
        // Save the client-requested tenant id BEFORE stripping so that ResolveTenantStep
        // can perform the mismatch check (client header vs. token claims) even after the
        // header has been removed from the request.
        context.ClientRequestedTenantId = TryGetHeader(
            context.HttpContext, tenantOptions.TenantIdHeaderName);

        context.HttpContext.Request.Headers.Remove(tenantOptions.TenantIdHeaderName);
        context.HttpContext.Request.Headers.Remove(EdgeHeaders.TenantDbStrategy);

        // Strip the inbound Authorization header on anonymous routes so that client bearer
        // tokens are never forwarded to upstream services unauthenticated. Authenticated
        // routes keep the header so ExchangeTokenStep can extract the user token to exchange.
        if (context.Policy.Mode == EdgeAccessMode.Anonymous)
        {
            context.HttpContext.Request.Headers.Remove("Authorization");
        }

        return Task.FromResult(EdgeStepResult.Proceed);
    }

    private static string? TryGetHeader(HttpContext http, string name) =>
        http.Request.Headers.TryGetValue(name, out var values) &&
        !string.IsNullOrWhiteSpace(values.ToString())
            ? values.ToString().Trim()
            : null;
}
