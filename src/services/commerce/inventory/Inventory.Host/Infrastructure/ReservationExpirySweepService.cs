using Inventories.Application.Inventory;
using Inventories.Application.Inventory.Features.ExpireHeldReservations.V1;
using Microsoft.Extensions.Options;
using Wolverine;

namespace Inventories.Host.Infrastructure;

/// <summary>
/// Periodically sweeps expired held reservations (Task 18 housekeeping): every
/// <see cref="InventoryOptions.SweepInterval"/>, invokes <see cref="ExpireHeldReservationsCommand"/>
/// in a fresh scope so the stored <c>StockItem.QuantityReserved</c> counter is corrected for holds
/// whose expiry already made them invisible to reads (Task 17's lazy expiry).
/// </summary>
/// <param name="scopeFactory">Factory used to create a fresh DI scope for each sweep tick.</param>
/// <param name="options">The inventory options, providing the sweep interval.</param>
/// <param name="logger">The logger used to record sweep activity and failures.</param>
public sealed class ReservationExpirySweepService(
    IServiceScopeFactory scopeFactory,
    IOptions<InventoryOptions> options,
    ILogger<ReservationExpirySweepService> logger) : BackgroundService
{
    /// <inheritdoc/>
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        TimeSpan interval = options.Value.SweepInterval;
        using var timer = new PeriodicTimer(interval);

        do
        {
            await RunSweepAsync(stoppingToken).ConfigureAwait(false);
        }
        while (await timer.WaitForNextTickAsync(stoppingToken).ConfigureAwait(false));
    }

    private async Task RunSweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
            var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();

            int expiredCount = await bus.InvokeAsync<int>(new ExpireHeldReservationsCommand(), stoppingToken).ConfigureAwait(false);

            if (expiredCount > 0)
            {
                logger.LogInformation("Reservation expiry sweep expired {ExpiredCount} held reservation(s).", expiredCount);
            }
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            // A single failed sweep must not take down the background service — the next tick
            // retries. Held reservations that outlive their hold simply stay lazily-ignored by
            // reads (Task 17) until a subsequent sweep succeeds.
            logger.LogError(ex, "Reservation expiry sweep failed.");
        }
    }
}
