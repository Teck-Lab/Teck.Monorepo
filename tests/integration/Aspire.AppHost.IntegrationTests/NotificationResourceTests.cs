using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Verifies Notification is bootstrapped as a first-class AppHost resource.</summary>
[Collection("AppHost")]
public sealed class NotificationResourceTests(AppHostFixture fixture)
{
    /// <summary>Starts Notification on Aspire's dynamic endpoint and checks its liveness surface.</summary>
    /// <returns>A task representing the asynchronous test.</returns>
    [Fact]
    public async Task Notification_IsReachable_WhenAppHostStarts()
    {
        var app = fixture.Application;

        try
        {
            await app.ResourceNotifications
                .WaitForResourceAsync("notification", KnownResourceStates.Running)
                .WaitAsync(TimeSpan.FromMinutes(5));
        }
        catch (TimeoutException ex)
        {
            // Report the dependency states rather than only the resource that failed to start.
            var states = new List<string>();
            foreach (var name in new[] { "postgres", "notificationdb", "keycloak", "rabbitmq", "notification", "gateway" })
            {
                states.Add(app.ResourceNotifications.TryGetCurrentState(name, out var resourceEvent)
                    ? $"{name}: {resourceEvent.Snapshot.State?.Text ?? "unknown"} ({resourceEvent.Snapshot.HealthStatus?.ToString() ?? "unknown"})"
                    : $"{name}: no state");
            }

            throw new TimeoutException($"Notification did not start. Resource states: {string.Join(", ", states)}", ex);
        }

        using var client = app.CreateHttpClient("notification", "http");
        var response = await client.GetAsync(new Uri("/alive", UriKind.Relative));

        Assert.True(response.IsSuccessStatusCode, $"GET /alive → {(int)response.StatusCode} {response.ReasonPhrase}");
    }
}
