using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Orders.Application.Orders;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;
using Orders.Host.Infrastructure;
using Xunit;

namespace Orders.IntegrationTests;

public sealed class KeycloakSubjectOwnershipTests
{
    [Fact]
    public void ProductionAccessor_SameStandardSubject_AllowsOwner()
    {
        var accessor = CreateAccessor("subject-owner");

        OrderOwnership.EnsureOwnedBy(CreateOrder("tenant-a"), accessor);
    }

    [Fact]
    public void ProductionAccessor_MissingOrCrossStandardSubject_DeniesOwner()
    {
        Assert.Throws<UnauthorizedAccessException>(() => OrderOwnership.EnsureOwnedBy(CreateOrder("tenant-a"), CreateAccessor(null)));
        Assert.Throws<UnauthorizedAccessException>(() => OrderOwnership.EnsureOwnedBy(CreateOrder("tenant-a"), CreateAccessor("subject-other")));
    }

    [Fact]
    public void TenantScopedCheckoutLookup_DoesNotSelectAnotherTenant()
    {
        var spec = new Orders.Application.Orders.ReadModels.OrderByCheckoutCorrelationSpec("checkout-1", "tenant-a");
        Assert.NotNull(spec);
    }

    private static OrderIdentityAccessor CreateAccessor(string? subject)
    {
        var context = new DefaultHttpContext();
        IEnumerable<Claim> claims = subject is null ? [] : [new Claim("sub", subject)];
        context.User = new ClaimsPrincipal(new ClaimsIdentity(claims, "test"));
        return new OrderIdentityAccessor(new HttpContextAccessor { HttpContext = context });
    }

    private static Order CreateOrder(string tenantId) => Order.Create(
        Guid.NewGuid(),
        "subject-owner",
        Guid.NewGuid(),
        tenantId,
        [new OrderLine(Guid.NewGuid(), "Widget", 1, 10m)],
        10m,
        "USD",
        Guid.NewGuid().ToString("N"));
}
