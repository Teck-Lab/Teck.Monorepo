using Inventories.Domain.ValueObjects;
using Xunit;

namespace Inventories.UnitTests;

public sealed class ReservationEnumsTests
{
    [Fact]
    public void ActiveStatuses_AreHeldAndCommitted()
    {
        Assert.True(ReservationStatus.Held.IsActive);
        Assert.True(ReservationStatus.Committed.IsActive);
        Assert.False(ReservationStatus.Expired.IsActive);
        Assert.False(ReservationStatus.Released.IsActive);
        Assert.False(ReservationStatus.Fulfilled.IsActive);
    }
}
