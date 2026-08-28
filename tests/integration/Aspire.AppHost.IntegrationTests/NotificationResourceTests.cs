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

        await app.ResourceNotifications
            .WaitForResourceAsync("notification", KnownResourceStates.Running)
            .WaitAsync(TimeSpan.FromMinutes(5));

        using var client = app.CreateHttpClient("notification", "http");
        var response = await client.GetAsync(new Uri("/alive", UriKind.Relative));

        Assert.True(response.IsSuccessStatusCode, $"GET /alive → {(int)response.StatusCode} {response.ReasonPhrase}");
    }
}
