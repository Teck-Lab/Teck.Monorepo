using Gateway.Public.Edge;
using Gateway.Public.Edge.Steps;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Gateway.Public.UnitTests.Edge.Steps;

/// <summary>Unit tests for <see cref="ResolveDbStrategyStep"/>.</summary>
public sealed class ResolveDbStrategyStepTests
{
    /// <summary>A resolved tenant id with a resolver that returns a successful strategy must set the header and proceed.</summary>
    [Fact]
    public async Task SetsDbStrategyHeaderAndProceeds_OnSuccess()
    {
        var http = new DefaultHttpContext();
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order"))
        {
            ResolvedTenantId = "t1",
        };
        var step = new ResolveDbStrategyStep(new FakeResolver(TenantDbStrategyResult.Ok("shared")));

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.True(result.Continue);
        Assert.Equal("shared", http.Request.Headers[EdgeHeaders.TenantDbStrategy].ToString());
        Assert.Equal("shared", ctx.DbStrategy);
    }

    /// <summary>A resolver failure must stop the pipeline with the mapped status code and error code.</summary>
    [Fact]
    public async Task StopsWithMappedError_OnResolverFailure()
    {
        var http = new DefaultHttpContext();
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order"))
        {
            ResolvedTenantId = "t1",
        };
        var step = new ResolveDbStrategyStep(
            new FakeResolver(TenantDbStrategyResult.Fail(404, "tenant.not_found", "Tenant not found.")));

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.False(result.Continue);
        Assert.Equal(404, result.Problem!.StatusCode);
        Assert.Equal("tenant.not_found", result.Problem.ErrorCode);
        Assert.Equal("Tenant not found.", result.Problem.Detail);
    }

    /// <summary>An anonymous policy must proceed without ever calling the resolver.</summary>
    [Fact]
    public async Task Anonymous_ProceedsWithoutCallingResolver()
    {
        var http = new DefaultHttpContext();
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Anonymous, null));
        var fake = new CountingFakeResolver(TenantDbStrategyResult.Ok("shared"));
        var step = new ResolveDbStrategyStep(fake);

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.True(result.Continue);
        Assert.Equal(0, fake.CallCount);
    }

    /// <summary>A blank ResolvedTenantId must proceed without calling the resolver.</summary>
    [Fact]
    public async Task BlankResolvedTenantId_ProceedsWithoutCallingResolver()
    {
        var http = new DefaultHttpContext();
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order"))
        {
            ResolvedTenantId = string.Empty,
        };
        var fake = new CountingFakeResolver(TenantDbStrategyResult.Ok("shared"));
        var step = new ResolveDbStrategyStep(fake);

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.True(result.Continue);
        Assert.Equal(0, fake.CallCount);
    }

    /// <summary>A failure result with no status code or error code must default to 503 / tenant.lookup.unavailable.</summary>
    [Fact]
    public async Task StopsWithDefaults_WhenFailureHasNoStatusOrCode()
    {
        var http = new DefaultHttpContext();
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order"))
        {
            ResolvedTenantId = "t1",
        };
        var step = new ResolveDbStrategyStep(
            new FakeResolver(new TenantDbStrategyResult(false, null, null, null, null)));

        EdgeStepResult result = await step.ExecuteAsync(ctx, default);

        Assert.False(result.Continue);
        Assert.Equal(503, result.Problem!.StatusCode);
        Assert.Equal("tenant.lookup.unavailable", result.Problem.ErrorCode);
    }

    /// <summary>The step must pass the ClusterId from context to the resolver as serviceName.</summary>
    [Fact]
    public async Task PassesClusterIdAsServiceName()
    {
        var http = new DefaultHttpContext();
        var ctx = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order"))
        {
            ResolvedTenantId = "t1",
            ClusterId = "customer-cluster",
        };
        string? capturedServiceName = null;
        var fake = new CapturingFakeResolver(TenantDbStrategyResult.Ok("dedicated"), (_, sn, _) => capturedServiceName = sn);
        var step = new ResolveDbStrategyStep(fake);

        await step.ExecuteAsync(ctx, default);

        Assert.Equal("customer-cluster", capturedServiceName);
    }

    private sealed class FakeResolver(TenantDbStrategyResult result) : ITenantDatabaseStrategyResolver
    {
        public Task<TenantDbStrategyResult> ResolveAsync(string tenantId, string? serviceName, CancellationToken ct) =>
            Task.FromResult(result);
    }

    private sealed class CountingFakeResolver(TenantDbStrategyResult result) : ITenantDatabaseStrategyResolver
    {
        public int CallCount { get; private set; }

        public Task<TenantDbStrategyResult> ResolveAsync(string tenantId, string? serviceName, CancellationToken ct)
        {
            CallCount++;
            return Task.FromResult(result);
        }
    }

    private sealed class CapturingFakeResolver(
        TenantDbStrategyResult result,
        Action<string, string?, CancellationToken> onResolve) : ITenantDatabaseStrategyResolver
    {
        public Task<TenantDbStrategyResult> ResolveAsync(string tenantId, string? serviceName, CancellationToken ct)
        {
            onResolve(tenantId, serviceName, ct);
            return Task.FromResult(result);
        }
    }
}
