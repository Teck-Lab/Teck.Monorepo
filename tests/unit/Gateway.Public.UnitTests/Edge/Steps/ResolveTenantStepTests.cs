using System.Security.Claims;
using Gateway.Public.Edge;
using Gateway.Public.Edge.Steps;
using Microsoft.AspNetCore.Http;
using SharedKernel.Infrastructure.MultiTenant;
using Xunit;

namespace Gateway.Public.UnitTests.Edge.Steps;

/// <summary>Unit tests for <see cref="ResolveTenantStep"/>.</summary>
public sealed class ResolveTenantStepTests
{
    private static readonly EdgeTenantOptions Options = new("X-TenantId", "organization", "tenant_id");

    private static ResolveTenantStep BuildStep(params string[] tenants) =>
        new(new FakeTenantResolver(tenants), Options);

    /// <summary>Authenticated request with no tenant claims must stop with 403 tenant.token.missing.</summary>
    [Fact]
    public async Task Authenticated_NoTokenTenants_Returns403TokenMissing()
    {
        var http = new DefaultHttpContext();
        var step = BuildStep();

        var result = await step.ExecuteAsync(
            new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order")), default);

        Assert.False(result.Continue);
        Assert.Equal("tenant.token.missing", result.Problem!.ErrorCode);
        Assert.Equal(403, result.Problem.StatusCode);
    }

    /// <summary>Authenticated request whose X-TenantId header is not in the token list must stop with 403 tenant.mismatch.</summary>
    [Fact]
    public async Task Authenticated_HeaderNotInToken_Returns403Mismatch()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-TenantId"] = "tenant-b";
        var step = BuildStep("tenant-a");

        var result = await step.ExecuteAsync(
            new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order")), default);

        Assert.False(result.Continue);
        Assert.Equal("tenant.mismatch", result.Problem!.ErrorCode);
        Assert.Equal(403, result.Problem.StatusCode);
    }

    /// <summary>Authenticated request whose X-TenantId header is in the token list must proceed with that tenant.</summary>
    [Fact]
    public async Task Authenticated_HeaderInToken_ProceedsWithHeaderTenant()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-TenantId"] = "tenant-a";
        var step = BuildStep("tenant-a", "tenant-b");
        var context = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order"));

        var result = await step.ExecuteAsync(context, default);

        Assert.True(result.Continue);
        Assert.Equal("tenant-a", context.ResolvedTenantId);
        Assert.Equal("tenant-a", http.Request.Headers["X-TenantId"].ToString());
        Assert.Equal("tenant-a", http.Items[EdgeHeaders.ResolvedTenantIdItemKey]);
    }

    /// <summary>Authenticated request with no header must proceed with the first tenant from the token.</summary>
    [Fact]
    public async Task Authenticated_NoHeader_ProceedsWithFirstTokenTenant()
    {
        var http = new DefaultHttpContext();
        var step = BuildStep("tenant-a", "tenant-b");
        var context = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order"));

        var result = await step.ExecuteAsync(context, default);

        Assert.True(result.Continue);
        Assert.Equal("tenant-a", context.ResolvedTenantId);
        Assert.Equal("tenant-a", http.Request.Headers["X-TenantId"].ToString());
        Assert.Equal("tenant-a", http.Items[EdgeHeaders.ResolvedTenantIdItemKey]);
    }

    /// <summary>TenantFromHeader request without the header must stop with 400 tenant.header.missing.</summary>
    [Fact]
    public async Task TenantFromHeader_NoHeader_Returns400HeaderMissing()
    {
        var http = new DefaultHttpContext();
        var step = BuildStep();

        var result = await step.ExecuteAsync(
            new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.TenantFromHeader, null)), default);

        Assert.False(result.Continue);
        Assert.Equal("tenant.header.missing", result.Problem!.ErrorCode);
        Assert.Equal(400, result.Problem.StatusCode);
    }

    /// <summary>TenantFromHeader request with the header must proceed and apply the tenant.</summary>
    [Fact]
    public async Task TenantFromHeader_HeaderPresent_Proceeds()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-TenantId"] = "tenant-a";
        var step = BuildStep();
        var context = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.TenantFromHeader, null));

        var result = await step.ExecuteAsync(context, default);

        Assert.True(result.Continue);
        Assert.Equal("tenant-a", context.ResolvedTenantId);
        Assert.Equal("tenant-a", http.Request.Headers["X-TenantId"].ToString());
        Assert.Equal("tenant-a", http.Items[EdgeHeaders.ResolvedTenantIdItemKey]);
    }

    /// <summary>Anonymous request must proceed without setting a tenant id.</summary>
    [Fact]
    public async Task Anonymous_ProceedsWithNoTenant()
    {
        var http = new DefaultHttpContext();
        var step = BuildStep();
        var context = new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Anonymous, null));

        var result = await step.ExecuteAsync(context, default);

        Assert.True(result.Continue);
        Assert.Null(context.ResolvedTenantId);
    }

    private sealed class FakeTenantResolver(params string[] tenants) : ITenantTokenContextResolver
    {
        public IReadOnlyList<string> ResolveTenantIds(ClaimsPrincipal user, string organizationClaimName, string tenantIdClaimName) =>
            tenants;
    }
}
