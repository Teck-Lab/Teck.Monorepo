using Baskets.Application.Baskets;
using Baskets.Domain.Entities;
using NSubstitute;
using Xunit;

namespace Baskets.UnitTests;

public sealed class BasketOwnershipTests
{
    [Fact]
    public void EnsureOwnedBy_MatchingStandardSubject_AllowsAccess()
    {
        var basket = Basket.CreateForSubject("shopper-subject", "tenant-1");
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.Subject.Returns("shopper-subject");

        BasketOwnership.EnsureOwnedBy(basket, identity);
    }

    [Fact]
    public void EnsureOwnedBy_DifferentSubject_RejectsAccess()
    {
        var basket = Basket.CreateForSubject("shopper-subject", "tenant-1");
        var identity = Substitute.For<IBasketIdentityAccessor>();
        identity.Subject.Returns("other-subject");

        Assert.Throws<UnauthorizedAccessException>(() => BasketOwnership.EnsureOwnedBy(basket, identity));
    }
}
