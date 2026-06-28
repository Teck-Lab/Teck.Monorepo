using Gateway.Public.Edge;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using Yarp.ReverseProxy.Configuration;
using Yarp.ReverseProxy.Forwarder;
using Yarp.ReverseProxy.Model;

namespace Gateway.Public.UnitTests.Edge;

/// <summary>Unit tests for <see cref="EdgeEnforcementMiddleware"/>.</summary>
public sealed class EdgeEnforcementMiddlewareTests
{
    private const string TestRouteId = "test-route";
    private const string TestClusterId = "test-cluster";

    // Steps are resolved per-request from context.RequestServices (scoped-safe pattern).
    private static DefaultHttpContext MakeHttpContext(params IEdgeStep[] steps)
    {
        var routeConfig = new RouteConfig { RouteId = TestRouteId, ClusterId = TestClusterId };
        var clusterState = new ClusterState(TestClusterId);
        var routeModel = new RouteModel(routeConfig, clusterState, HttpTransformer.Default);

        var http = new DefaultHttpContext();
        http.Response.Body = new MemoryStream();
        http.Features.Set<IReverseProxyFeature>(new FakeProxyFeature(routeModel));

        var services = new ServiceCollection();
        foreach (IEdgeStep step in steps)
        {
            services.AddSingleton<IEdgeStep>(step);
        }

        http.RequestServices = services.BuildServiceProvider();
        return http;
    }

    private static EdgeEnforcementMiddleware BuildMiddleware(RequestDelegate next)
    {
        var policy = new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order");
        return new EdgeEnforcementMiddleware(next, new FakeRegistry(TestRouteId, policy));
    }

    /// <summary>A step that stops with 403 must not call the next delegate and must write the problem to the response.</summary>
    [Fact]
    public async Task StopStep_DoesNotCallNext_AndWritesProblem()
    {
        var http = MakeHttpContext(new StopStep(403, "test.denied"));
        var nextCalled = false;
        Task Next(HttpContext _) { nextCalled = true; return Task.CompletedTask; }

        var middleware = BuildMiddleware(Next);
        await middleware.InvokeAsync(http);

        Assert.False(nextCalled);
        Assert.Equal(403, http.Response.StatusCode);
    }

    /// <summary>When all steps proceed, the next delegate must be called.</summary>
    [Fact]
    public async Task AllProceedSteps_CallNext()
    {
        var http = MakeHttpContext(new ProceedStep(), new ProceedStep());
        var nextCalled = false;
        Task Next(HttpContext _) { nextCalled = true; return Task.CompletedTask; }

        var middleware = BuildMiddleware(Next);
        await middleware.InvokeAsync(http);

        Assert.True(nextCalled);
    }

    private sealed class FakeProxyFeature(RouteModel route) : IReverseProxyFeature
    {
        public RouteModel Route => route;

        public ClusterModel Cluster => null!;

        public IReadOnlyList<DestinationState> AllDestinations => [];

        public IReadOnlyList<DestinationState> AvailableDestinations { get; set; } = [];

        public DestinationState? ProxiedDestination { get; set; }
    }

    private sealed class FakeRegistry(string routeId, EdgeAccessPolicy policy) : IEdgeAccessPolicyRegistry
    {
        public EdgeAccessPolicy? ForRoute(string id) => id == routeId ? policy : null;
    }

    private sealed class StopStep(int statusCode, string errorCode) : IEdgeStep
    {
        public Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct) =>
            Task.FromResult(EdgeStepResult.Stop(new EdgeProblem(statusCode, "Denied", "Access denied.", errorCode)));
    }

    private sealed class ProceedStep : IEdgeStep
    {
        public Task<EdgeStepResult> ExecuteAsync(EdgeContext context, CancellationToken ct) =>
            Task.FromResult(EdgeStepResult.Proceed);
    }
}
