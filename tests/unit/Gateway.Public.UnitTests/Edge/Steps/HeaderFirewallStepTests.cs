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
}
