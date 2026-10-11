using Aspire.Hosting;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Starts one AppHost application for all AppHost collection assertions and disposes it after the collection completes.</summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    private DistributedApplication? application;

    /// <summary>Gets the already-started shared AppHost application.</summary>
    internal DistributedApplication Application => application ?? throw new InvalidOperationException("Fixture initialization has not completed.");

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var appHost = await AppHostTestBuilder.CreateAsync().ConfigureAwait(false);
        application = await appHost.BuildAsync().ConfigureAwait(false);
        await application.StartAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (application is not null)
        {
            await application.DisposeAsync().ConfigureAwait(false);
        }
    }
}
