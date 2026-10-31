using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Retries approved but not yet backup-verified tenant purges independently of broker delivery.</summary>
/// <param name="store">Durable signed-approval queue.</param>
/// <param name="services">Resolves restoration dependencies only after an approval is pending.</param>
/// <param name="logger">Non-sensitive error and progress reporting.</param>
/// <param name="options">Worker polling interval.</param>
public sealed class TenantPurgePollingService(ITenantPurgeStore store, IServiceProvider services,
    ILogger<TenantPurgePollingService> logger, IOptions<TenantDatabasePollingOptions> options) : BackgroundService
{
    /// <summary>Attempts each pending approval independently so one unavailable backup cannot block other tenants.</summary>
    /// <param name="ct">Cancellation signal.</param>
    /// <returns>Number of queued approvals inspected.</returns>
    public async Task<int> ProcessBatchAsync(CancellationToken ct)
    {
        var pending = await store.GetPendingPurgeApprovalsAsync(16, ct).ConfigureAwait(false);
        foreach (var approval in pending)
        {
            try
            {
                await services.GetRequiredService<TenantPurgeCoordinator>().PurgeAsync(approval, ct).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Tenant purge for {TenantId} and source order {SourceOrderId} remains queued; no completion acknowledged.",
                    approval.TenantId, approval.SourceOrderId);
            }
        }

        return pending.Count;
    }

    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = options.Value.PollInterval;
        if (interval <= TimeSpan.Zero) throw new InvalidOperationException("Tenant purge polling interval must be positive.");
        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await ProcessBatchAsync(stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                break;
            }
            catch (Exception error)
            {
                logger.LogError(error, "Tenant purge approval queue is unavailable; approved requests remain durable.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
