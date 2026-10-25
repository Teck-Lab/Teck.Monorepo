using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>
/// Aspire AppHost smoke test: boots the full stack and verifies both web gateways
/// start and respond on their liveness endpoints.
///
/// No HTTP health checks are configured on resources in AppHost.cs, so project
/// resources reach Running rather than Healthy. Each gateway maps /alive through
/// MapDefaultEndpoints without requiring authentication.
/// </summary>
[Collection("AppHost")]
public sealed class AppHostSmokeTests(AppHostFixture fixture)
{
    [Theory]
    [InlineData("web-customer")]
    [InlineData("web-admin")]
    public async Task WebGateway_IsReachable_WhenAppHostStarts(string resourceName)
    {
        var app = fixture.Application;

        // A named HTTP endpoint lets Aspire resolve each gateway's dynamic port.
        await app.ResourceNotifications
            .WaitForResourceAsync(resourceName, KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var client = app.CreateHttpClient(resourceName, "http");

        // /alive is mapped by MapDefaultEndpoints() in TeckServiceDefaultsExtensions
        // (health checks tagged "live").  It is always accessible without authentication
        // and does not proxy to any downstream service, making it the ideal smoke assertion.
        var response = await client.GetAsync(new Uri("/alive", UriKind.Relative));

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET /alive → {(int)response.StatusCode} {response.ReasonPhrase}");
    }
}
