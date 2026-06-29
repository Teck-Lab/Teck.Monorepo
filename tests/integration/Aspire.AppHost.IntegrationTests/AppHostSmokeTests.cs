using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>
/// Aspire AppHost smoke test: boots the full stack (Postgres, Keycloak, Redis, RabbitMQ,
/// order/customer/catalog/gateway) and verifies the gateway process starts and responds on
/// its liveness endpoint.
///
/// No HTTP health checks are configured on resources in AppHost.cs (no WithHttpHealthCheck),
/// so resources do not transition to HealthStatus.Healthy via an HTTP probe.  The test waits
/// for <see cref="KnownResourceStates.Running"/> instead, which is the terminal state for
/// project resources without explicit health probes.  Once Running, the gateway's /alive
/// liveness endpoint (mapped by <c>MapDefaultEndpoints</c> in TeckServiceDefaults) is
/// asserted reachable and returning 200 OK.
///
/// First run pulls Docker images and builds four services — allow up to 10 minutes end-to-end.
/// </summary>
public sealed class AppHostSmokeTests
{
    [Fact]
    public async Task Gateway_IsReachable_WhenAppHostStarts()
    {
        // Build and start the entire distributed application.
        // DistributedApplicationTestingBuilder launches all resources defined in AppHost.cs
        // inside the test process using the Aspire orchestration layer.
        var appHost = await DistributedApplicationTestingBuilder
            .CreateAsync<Projects.Teck_AppHost>();

        await using var app = await appHost.BuildAsync();
        await app.StartAsync();

        // Wait for the gateway resource to reach the Running state.
        // AppHost.cs does not call WithHttpHealthCheck on the gateway, so Aspire never
        // transitions it to HealthStatus.Healthy via an HTTP probe — it stops at Running.
        // WaitForResourceAsync(..., KnownResourceStates.Running) is the correct wait here.
        // The gateway itself depends on order/customer/catalog which depend on their DBs and
        // Keycloak, so this implicitly waits for the full dependency chain.
        await app.ResourceNotifications
            .WaitForResourceAsync("gateway", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(10));

        // Ask Aspire for an HttpClient that resolves to the gateway's dynamic port.
        // "http" matches the WithHttpEndpoint(name: "http") configured in AppHost.cs.
        using var client = app.CreateHttpClient("gateway", "http");

        // /alive is mapped by MapDefaultEndpoints() in TeckServiceDefaultsExtensions
        // (health checks tagged "live").  It is always accessible without authentication
        // and does not proxy to any downstream service, making it the ideal smoke assertion.
        var response = await client.GetAsync(new Uri("/alive", UriKind.Relative));

        Assert.True(
            response.IsSuccessStatusCode,
            $"GET /alive → {(int)response.StatusCode} {response.ReasonPhrase}");
    }
}
