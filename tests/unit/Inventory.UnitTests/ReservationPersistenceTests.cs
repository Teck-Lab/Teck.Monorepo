using Finbuckle.MultiTenant.Abstractions;
using Inventories.Application.Database;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using NSubstitute;
using SharedKernel.Infrastructure.MultiTenant;
using Xunit;

namespace Inventories.UnitTests;

/// <summary>
/// Verifies that <see cref="Reservation"/> (with owned <see cref="ReservationLine"/> and nested
/// owned <see cref="Allocation"/> collections) and <see cref="LocationPriority"/> round-trip
/// through EF Core.
/// </summary>
public sealed class ReservationPersistenceTests
{
    [Fact]
    public void Model_BuildsWithoutError()
    {
        using var db = CreateInMemory($"reservation-model-{Guid.NewGuid()}");

        Assert.NotNull(db.Model);
        Assert.NotNull(db.Model.FindEntityType(typeof(Reservation)));
        Assert.NotNull(db.Model.FindEntityType(typeof(LocationPriority)));
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsReservationLinesAndAllocations()
    {
        var name = $"reservation-roundtrip-{Guid.NewGuid()}";
        var productA = Guid.NewGuid();
        var productB = Guid.NewGuid();
        var locationA = Guid.NewGuid();
        var locationB = Guid.NewGuid();

        var lines = new List<ReservationLine>
        {
            new(
                ProductId: productA,
                RequestedQuantity: 5,
                BackorderedQuantity: 0,
                Allocations:
                [
                    new Allocation(locationA, 3),
                    new Allocation(locationB, 2),
                ]),
            new(
                ProductId: productB,
                RequestedQuantity: 4,
                BackorderedQuantity: 1,
                Allocations:
                [
                    new Allocation(locationA, 3),
                ]),
        };

        var reservation = Reservation.CreateCommitted(
            ReservationSource.Basket,
            Guid.NewGuid(),
            "tenant-1",
            lines);

        using (var db = CreateInMemory(name))
        {
            db.Reservations.Add(reservation);
            await db.SaveChangesAsync();
        }

        using (var db = CreateInMemory(name))
        {
            Reservation? reloaded = await db.Reservations
                .Include(r => r.Lines)
                .FirstOrDefaultAsync();

            Assert.NotNull(reloaded);
            Assert.Equal("tenant-1", reloaded!.TenantId);
            Assert.Equal(ReservationSource.Basket, reloaded.SourceType);
            Assert.Equal(ReservationStatus.Committed, reloaded.Status);
            Assert.Equal(2, reloaded.Lines.Count);

            ReservationLine lineA = Assert.Single(reloaded.Lines, l => l.ProductId == productA);
            Assert.Equal(5, lineA.RequestedQuantity);
            Assert.Equal(0, lineA.BackorderedQuantity);
            Assert.Equal(2, lineA.Allocations.Count);
            Assert.Contains(lineA.Allocations, a => a.LocationId == locationA && a.Quantity == 3);
            Assert.Contains(lineA.Allocations, a => a.LocationId == locationB && a.Quantity == 2);

            ReservationLine lineB = Assert.Single(reloaded.Lines, l => l.ProductId == productB);
            Assert.Equal(4, lineB.RequestedQuantity);
            Assert.Equal(1, lineB.BackorderedQuantity);
            Assert.Single(lineB.Allocations);
        }
    }

    [Fact]
    public async Task SaveAndReload_RoundTripsLocationPriorityOrder()
    {
        var name = $"location-priority-roundtrip-{Guid.NewGuid()}";
        var ordered = new List<Guid> { Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid() };
        LocationPriority priority = LocationPriority.Create("tenant-1", ordered);

        using (var db = CreateInMemory(name))
        {
            db.LocationPriorities.Add(priority);
            await db.SaveChangesAsync();
        }

        using (var db = CreateInMemory(name))
        {
            LocationPriority? reloaded = await db.LocationPriorities.FirstOrDefaultAsync();

            Assert.NotNull(reloaded);
            Assert.Equal("tenant-1", reloaded!.TenantId);
            Assert.Equal(ordered, reloaded.LocationIds);
        }
    }

    private static InventoryDbContext CreateInMemory(string name)
    {
        var options = new DbContextOptionsBuilder<InventoryDbContext>()
            .UseInMemoryDatabase(name)
            .Options;

        return new InventoryDbContext(options, TenantAccessor());
    }

    private static IMultiTenantContextAccessor<TenantDetails> TenantAccessor()
    {
        var accessor = Substitute.For<IMultiTenantContextAccessor<TenantDetails>>();
        accessor.MultiTenantContext.Returns(new MultiTenantContext<TenantDetails>(new TenantDetails { Id = "tenant-1", Identifier = "tenant-1" }));
        return accessor;
    }
}
