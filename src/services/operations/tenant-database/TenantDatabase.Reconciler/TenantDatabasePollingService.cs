using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Teck.TenantDatabase.Reconciler;

public sealed class TenantDatabasePollingOptions
{
    public TimeSpan PollInterval { get; set; } = TimeSpan.FromSeconds(15);
}

/// <summary>Continuously retries durable requests independently of event-delivery lifetime.</summary>
public sealed class TenantDatabasePollingService(TenantDatabaseWorker worker, ILogger<TenantDatabasePollingService> logger, IOptions<TenantDatabasePollingOptions>? options = null) : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan pollInterval = options?.Value.PollInterval ?? TimeSpan.FromSeconds(15);
        if (pollInterval <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(options), "Poll interval must be positive.");
        using var timer = new PeriodicTimer(pollInterval);
        do
        {
            try
            {
                await worker.ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Tenant database reconciliation batch failed; requests remain durable for retry.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
