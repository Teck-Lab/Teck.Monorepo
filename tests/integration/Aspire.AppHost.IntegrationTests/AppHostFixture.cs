using Aspire.Hosting;
using Aspire.Hosting.ApplicationModel;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Starts one AppHost application for all AppHost collection assertions and disposes it after the collection completes.</summary>
public sealed class AppHostFixture : IAsyncLifetime
{
    private DistributedApplication? application;
    private readonly List<string> notificationLogs = [];
    private CancellationTokenSource? logCancellation;
    private Task? logCapture;

    /// <summary>Gets the already-started shared AppHost application.</summary>
    internal DistributedApplication Application => application ?? throw new InvalidOperationException("Fixture initialization has not completed.");

    /// <summary>Gets the most recent Notification process output captured before and during application startup.</summary>
    internal string[] NotificationLogs
    {
        get
        {
            lock (notificationLogs)
            {
                return notificationLogs.TakeLast(80).ToArray();
            }
        }
    }

    /// <inheritdoc />
    public async ValueTask InitializeAsync()
    {
        var appHost = await AppHostTestBuilder.CreateAsync().ConfigureAwait(false);
        application = await appHost.BuildAsync().ConfigureAwait(false);
        logCancellation = new CancellationTokenSource();
        logCapture = CaptureNotificationLogsAsync(application.Services.GetRequiredService<ResourceLoggerService>(), logCancellation.Token);
        await application.StartAsync().ConfigureAwait(false);
    }

    /// <inheritdoc />
    public async ValueTask DisposeAsync()
    {
        if (logCancellation is not null)
        {
            await logCancellation.CancelAsync().ConfigureAwait(false);
            if (logCapture is not null)
            {
                await logCapture.ConfigureAwait(false);
            }
            logCancellation.Dispose();
        }

        if (application is not null)
        {
            await application.DisposeAsync().ConfigureAwait(false);
        }
    }

    private async Task CaptureNotificationLogsAsync(ResourceLoggerService logger, CancellationToken cancellationToken)
    {
        try
        {
            await foreach (var batch in logger.WatchAsync("notification").WithCancellation(cancellationToken).ConfigureAwait(false))
            {
                lock (notificationLogs)
                {
                    notificationLogs.AddRange(batch.Select(line => line.Content));
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
    }
}
