using NSubstitute;
using Orders.Application.Orders;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using Xunit;

namespace Orders.UnitTests;

public sealed class OrderOwnershipTests
{
    [Fact]
    public void EnsureOwnedBy_SameStandardSubject_Succeeds()
    {
        var identity = Substitute.For<IOrderIdentityAccessor>();
        identity.Subject.Returns("subject-owner");

        OrderOwnership.EnsureOwnedBy(CreateOrder(), identity);
    }

    [Fact]
    public void EnsureOwnedBy_MissingStandardSubject_Throws()
    {
        var identity = Substitute.For<IOrderIdentityAccessor>();
        identity.Subject.Returns((string?)null);

        Assert.Throws<UnauthorizedAccessException>(() => OrderOwnership.EnsureOwnedBy(CreateOrder(), identity));
    }

    [Fact]
    public void EnsureOwnedBy_CrossSubject_Throws()
    {
        var identity = Substitute.For<IOrderIdentityAccessor>();
        identity.Subject.Returns("subject-other");

        Assert.Throws<UnauthorizedAccessException>(() => OrderOwnership.EnsureOwnedBy(CreateOrder(), identity));
    }

    private static Order CreateOrder() => Order.Create(
        Guid.NewGuid(),
        "subject-owner",
        Guid.NewGuid(),
        "tenant-1",
        [new OrderLine(Guid.NewGuid(), "Widget", 1, 10m)],
        10m,
        "USD",
        "checkout-owner");
}
