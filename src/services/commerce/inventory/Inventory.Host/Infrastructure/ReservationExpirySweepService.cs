using Finbuckle.MultiTenant.Abstractions;
using Inventories.Application.Database;
using Inventories.Application.Inventory;
using Inventories.Application.Inventory.Features.ExpireHeldReservations.V1;
using Microsoft.Extensions.Options;
using SharedKernel.Infrastructure.MultiTenant;
using Wolverine;

namespace Inventories.Host.Infrastructure;

/// <summary>
/// Periodically sweeps expired held reservations (Task 18 housekeeping): every
/// <see cref="InventoryOptions.SweepInterval"/>, invokes <see cref="ExpireHeldReservationsCommand"/>
/// without a tenant only to discover owning tenant ids, then invokes the production handler in one
/// fresh tenant scope per id so the stored <c>StockItem.QuantityReserved</c> counter is corrected.
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

    private static void SetTenantContext(IServiceProvider services, string tenantId)
    {
        services.GetRequiredService<IMultiTenantContextSetter>().MultiTenantContext =
            new MultiTenantContext<TenantDetails>(new TenantDetails
            {
                Id = tenantId,
                Identifier = tenantId,
                Name = tenantId,
                IsActive = true,
            });
    }

    private async Task RunSweepAsync(CancellationToken stoppingToken)
    {
        try
        {
            IReadOnlyList<string> tenantIds = await DiscoverTenantIdsAsync(stoppingToken).ConfigureAwait(false);
            int expiredCount = 0;
            foreach (string tenantId in tenantIds)
            {
                await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
                SetTenantContext(scope.ServiceProvider, tenantId);
                var bus = scope.ServiceProvider.GetRequiredService<IMessageBus>();
                expiredCount += await bus.InvokeAsync<int>(new ExpireHeldReservationsCommand(tenantId), stoppingToken).ConfigureAwait(false);
            }

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

    private async Task<IReadOnlyList<string>> DiscoverTenantIdsAsync(CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopeFactory.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<InventoryDbContext>();
        DateTimeOffset now = TimeProvider.System.GetUtcNow();
        return await db.FindTenantsWithExpiredReservationsAsync(now, ct).ConfigureAwait(false);
    }
}
