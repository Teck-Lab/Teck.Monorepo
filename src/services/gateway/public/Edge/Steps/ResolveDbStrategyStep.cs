namespace Gateway.Public.Edge.Steps;

/// <summary>Resolves the tenant DB strategy and forwards it as a trusted header.</summary>
/// <param name="resolver">The strategy resolver.</param>
public sealed class ResolveDbStrategyStep(ITenantDatabaseStrategyResolver resolver) : IEdgeStep
{
    private readonly ITenantDatabaseStrategyResolver resolver = resolver;

    /// <inheritdoc/>
    public async Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct)
    {
        if (context.Policy.Mode == EdgeAccessMode.Anonymous || string.IsNullOrWhiteSpace(context.ResolvedTenantId))
        {
            return EdgeStepResult.Proceed;
        }

        TenantDbStrategyResult result = await resolver
            .ResolveAsync(context.ResolvedTenantId!, context.ClusterId, ct)
            .ConfigureAwait(false);

        if (!result.Success)
        {
            return EdgeStepResult.Stop(new EdgeProblem(
                result.StatusCode ?? 503,
                "Tenant lookup failed",
                result.ErrorDetail ?? "Unable to resolve tenant database strategy.",
                result.ErrorCode ?? "tenant.lookup.unavailable"));
        }

        context.DbStrategy = result.DatabaseStrategy;
        context.HttpContext.Request.Headers[EdgeHeaders.TenantDbStrategy] = result.DatabaseStrategy;
        return EdgeStepResult.Proceed;
    }
}
