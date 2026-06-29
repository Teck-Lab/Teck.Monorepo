using Yarp.ReverseProxy.Model;

namespace Gateway.Public.Edge;

/// <summary>Runs the ordered edge step pipeline for proxied routes.</summary>
public sealed class EdgeEnforcementMiddleware
{
    private readonly RequestDelegate next;
    private readonly IEdgeAccessPolicyRegistry registry;

    /// <summary>Initializes a new instance of the <see cref="EdgeEnforcementMiddleware"/> class.</summary>
    /// <param name="next">The next delegate.</param>
    /// <param name="registry">The route policy registry.</param>
    public EdgeEnforcementMiddleware(RequestDelegate next, IEdgeAccessPolicyRegistry registry)
    {
        this.next = next;
        this.registry = registry;
    }

    /// <summary>Executes the middleware.</summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task.</returns>
    public async Task InvokeAsync(HttpContext context)
    {
        var routeConfig = context.Features.Get<IReverseProxyFeature>()?.Route?.Config;

        if (routeConfig is null)
        {
            await next(context);
            return;
        }

        EdgeAccessPolicy? policy = registry.ForRoute(routeConfig.RouteId);
        if (policy is null)
        {
            await next(context);
            return;
        }

        var edge = new EdgeContext(context, policy)
        {
            ClusterId = routeConfig.ClusterId,
        };

        // Resolve steps from the per-request DI scope so scoped services are honoured and
        // registration order (HeaderFirewall → ResolveTenant → ResolveDbStrategy → ExchangeToken)
        // is preserved via IEnumerable<IEdgeStep>.
        var steps = context.RequestServices.GetRequiredService<IEnumerable<IEdgeStep>>();
        foreach (IEdgeStep step in steps)
        {
            EdgeStepResult result = await step.ExecuteAsync(edge, context.RequestAborted);
            if (!result.Continue)
            {
                await EdgeProblemWriter.WriteAsync(context, result.Problem!);
                return;
            }
        }

        await next(context);
    }
}
