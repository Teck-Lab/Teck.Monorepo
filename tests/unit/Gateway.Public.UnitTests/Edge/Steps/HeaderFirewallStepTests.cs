using Gateway.Public.Edge;
using Gateway.Public.Edge.Steps;
using Microsoft.AspNetCore.Http;
using Xunit;

namespace Gateway.Public.UnitTests.Edge.Steps;

/// <summary>Unit tests for <see cref="HeaderFirewallStep"/>.</summary>
public sealed class HeaderFirewallStepTests
{
    /// <summary>Spoofed trusted headers must be stripped before any downstream processing.</summary>
    [Fact]
    public async Task RemovesClientSuppliedTrustHeaders()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-TenantId"] = "spoofed";
        http.Request.Headers["X-Tenant-DbStrategy"] = "spoofed";
        var options = new EdgeTenantOptions("X-TenantId", "organization", "tenant_id");
        var step = new HeaderFirewallStep(options);

        var result = await step.ExecuteAsync(new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order")), default);

        Assert.True(result.Continue);
        Assert.False(http.Request.Headers.ContainsKey("X-TenantId"));
        Assert.False(http.Request.Headers.ContainsKey("X-Tenant-DbStrategy"));
    }

    /// <summary>On anonymous routes the inbound Authorization header must be stripped so bearer tokens never reach upstream unauthenticated.</summary>
    [Fact]
    public async Task AnonymousRoute_StripsAuthorizationHeader()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-TenantId"] = "spoofed";
        http.Request.Headers["Authorization"] = "Bearer client-token";
        var options = new EdgeTenantOptions("X-TenantId", "organization", "tenant_id");
        var step = new HeaderFirewallStep(options);

        await step.ExecuteAsync(new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Anonymous, null)), default);

        Assert.False(http.Request.Headers.ContainsKey("Authorization"));
    }

    /// <summary>On authenticated routes the inbound Authorization header must be preserved so ExchangeTokenStep can read the user token.</summary>
    [Fact]
    public async Task AuthenticatedRoute_PreservesAuthorizationHeader()
    {
        var http = new DefaultHttpContext();
        http.Request.Headers["X-TenantId"] = "spoofed";
        http.Request.Headers["Authorization"] = "Bearer user-token";
        var options = new EdgeTenantOptions("X-TenantId", "organization", "tenant_id");
        var step = new HeaderFirewallStep(options);

        await step.ExecuteAsync(new EdgeContext(http, new EdgeAccessPolicy(EdgeAccessMode.Authenticated, "order")), default);

        Assert.True(http.Request.Headers.ContainsKey("Authorization"));
        Assert.Equal("Bearer user-token", http.Request.Headers.Authorization.ToString());
    }
}
