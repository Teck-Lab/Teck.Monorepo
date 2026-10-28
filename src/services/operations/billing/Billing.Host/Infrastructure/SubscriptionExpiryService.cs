using Billings.Application.Billing.Purchases;
using Wolverine;

namespace Billings.Host.Infrastructure;

/// <summary>Reconciles paid subscription expiry after the validity window elapses.</summary>
/// <param name="services">The Billing service scope factory.</param>
/// <param name="logger">The service logger.</param>
/// <param name="configuration">Billing expiry polling configuration.</param>
public sealed class SubscriptionExpiryService(IServiceScopeFactory services, ILogger<SubscriptionExpiryService> logger, IConfiguration configuration)
    : BackgroundService
{
    /// <inheritdoc />
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        var interval = configuration.GetValue<TimeSpan?>("SubscriptionExpiry:PollInterval") ?? TimeSpan.FromHours(1);
        if (interval <= TimeSpan.Zero)
        {
            throw new InvalidOperationException("Subscription expiry poll interval must be positive.");
        }

        using var timer = new PeriodicTimer(interval);
        do
        {
            try
            {
                await using var scope = services.CreateAsyncScope();
                await scope.ServiceProvider.GetRequiredService<IMessageBus>()
                    .InvokeAsync(new SuspendExpiredPurchasesCommand(DateTimeOffset.UtcNow), stoppingToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
            {
                return;
            }
            catch (Exception exception)
            {
                logger.LogError(exception, "Failed to reconcile expired paid subscriptions; retrying on the next interval.");
            }
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }
}
