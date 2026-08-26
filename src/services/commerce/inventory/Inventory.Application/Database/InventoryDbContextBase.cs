using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;

namespace Inventories.Application.Database;

/// <summary>
/// Abstract inventory context that defines the entity model exactly once. The write and read
/// contexts derive from it.
/// </summary>
/// <param name="options">The options used to configure the context.</param>
/// <param name="tenantContextAccessor">The accessor used to resolve the current tenant.</param>
public abstract class InventoryDbContextBase(DbContextOptions options, IMultiTenantContextAccessor<TenantDetails> tenantContextAccessor)
    : BaseDbContext(options, tenantAccessor: tenantContextAccessor)
{
    /// <summary>Gets the set of tracked stock items.</summary>
    public DbSet<StockItem> StockItems => Set<StockItem>();

    /// <summary>Gets the set of tracked stock reservations.</summary>
    public DbSet<Reservation> Reservations => Set<Reservation>();

    /// <summary>Gets the set of tracked location priority lists.</summary>
    public DbSet<LocationPriority> LocationPriorities => Set<LocationPriority>();

    /// <summary>
    /// Finds tenants that own a reservation requiring expiry processing at <paramref name="asOf"/>.
    /// The named tenant-filter bypass is limited to discovery; the caller must establish each
    /// returned tenant before issuing the mutating expiry command.
    /// </summary>
    /// <param name="asOf">The instant against which reservation expiry is evaluated.</param>
    /// <param name="cancellationToken">Token used to cancel the database query.</param>
    /// <returns>The distinct tenant identifiers requiring expiry processing.</returns>
    public async Task<IReadOnlyList<string>> FindTenantsWithExpiredReservationsAsync(
        DateTimeOffset asOf,
        CancellationToken cancellationToken = default)
    {
        return await Reservations
            .AsNoTracking()
            .IgnoreQueryFilters([Constants.TenantToken])
            .Where(reservation =>
                (reservation.Status == ReservationStatus.Held && reservation.ExpiresAt <= asOf) ||
                (reservation.SourceType == ReservationSource.Order &&
                 reservation.Status == ReservationStatus.Committed &&
                 reservation.BackorderExpiresAt <= asOf &&
                 reservation.Lines.Any(line => line.BackorderedQuantity > 0)))
            .Select(reservation => reservation.TenantId)
            .Distinct()
            .ToListAsync(cancellationToken)
            .ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.ApplyConfigurationsFromAssembly(typeof(InventoryDbContextBase).Assembly);
        base.OnModelCreating(modelBuilder);
        modelBuilder.Entity<StockItem>().IsMultiTenant();
        modelBuilder.Entity<Reservation>().IsMultiTenant();
        modelBuilder.Entity<LocationPriority>().IsMultiTenant();
    }
}
