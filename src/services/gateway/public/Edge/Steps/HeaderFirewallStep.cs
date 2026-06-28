namespace Gateway.Public.Edge.Steps;

/// <summary>Strips client-supplied trusted internal headers so only the gateway can set them.</summary>
/// <param name="tenantOptions">The edge tenant options.</param>
public sealed class HeaderFirewallStep(EdgeTenantOptions tenantOptions) : IEdgeStep
{
    private readonly EdgeTenantOptions tenantOptions = tenantOptions;

    /// <inheritdoc/>
    public Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct)
    {
        context.HttpContext.Request.Headers.Remove(tenantOptions.TenantIdHeaderName);
        context.HttpContext.Request.Headers.Remove(EdgeHeaders.TenantDbStrategy);
        return Task.FromResult(EdgeStepResult.Proceed);
    }
}
