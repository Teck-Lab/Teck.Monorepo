using Billings.Domain.Entities;
using Xunit;

namespace Billing.UnitTests.Domain;

public sealed class PaymentStatusTests
{
    [Fact]
    public void List_ContainsExactlyFiveStatuses()
    {
        var statuses = PaymentStatus.List;

        Assert.Equal(5, statuses.Count);
    }

    [Theory]
    [InlineData(1, "Pending")]
    [InlineData(2, "Authorized")]
    [InlineData(3, "Captured")]
    [InlineData(4, "Failed")]
    [InlineData(5, "Refunded")]
    public void FromValue_RoundTrips(int value, string name)
    {
        var status = PaymentStatus.FromValue(value);

        Assert.Equal(name, status.Name);
        Assert.Equal(value, status.Value);
    }
}
