using Ardalis.Specification;
using Customers.Application.Tenants.ReadModels;
using Customers.Domain.Entities;
using Xunit;

namespace Customer.UnitTests.Tenants;

/// <summary>Tests for <see cref="TenantByIdSpec"/>.</summary>
public sealed class TenantByIdSpecTests
{
    /// <summary>The spec must match only the tenant whose id was supplied at construction.</summary>
    [Fact]
    public void Matches_OnlyTheTenantWithTheGivenId()
    {
        var wanted = Tenant.Create(Guid.NewGuid(), "acme", "shared", "postgres", false);
        var other = Tenant.Create(Guid.NewGuid(), "other", "dedicated", "postgres", true);
        var spec = new TenantByIdSpec(wanted.Id);

        var result = spec.Evaluate(new[] { wanted, other }).ToList();

        Assert.Single(result);
        Assert.Equal(wanted.Id, result[0].Id);
    }
}
