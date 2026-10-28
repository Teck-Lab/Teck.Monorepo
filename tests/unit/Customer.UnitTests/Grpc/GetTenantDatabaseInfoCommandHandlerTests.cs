using Customers.Domain.Entities;
using Customers.Host.Grpc.V1;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using Xunit;

namespace Customer.UnitTests.Grpc;

/// <summary>Tests for <see cref="GetTenantDatabaseInfoCommandHandler"/>.</summary>
public sealed class GetTenantDatabaseInfoCommandHandlerTests
{
    /// <summary>An invalid GUID tenant ID must yield a not-found result containing "GUID" in the error detail.</summary>
    [Fact]
    public async Task ReturnsNotFound_ForInvalidGuid()
    {
        var handler = new GetTenantDatabaseInfoCommandHandler(new FakeTenantReadRepository(null));
        var result = await handler.ExecuteAsync(new GetTenantDatabaseInfoCommand { TenantId = "not-a-guid" }, default);

        Assert.False(result.Found);
        Assert.Contains("GUID", result.ErrorDetail);
    }

    /// <summary>A valid GUID that does not match any tenant must yield a not-found result.</summary>
    [Fact]
    public async Task ReturnsNotFound_WhenTenantMissing()
    {
        var handler = new GetTenantDatabaseInfoCommandHandler(new FakeTenantReadRepository(null));
        var result = await handler.ExecuteAsync(
            new GetTenantDatabaseInfoCommand { TenantId = Guid.NewGuid().ToString() }, default);

        Assert.False(result.Found);
    }

    /// <summary>A reserved tenant must not expose database routing before payment and provisioning.</summary>
    [Fact]
    public async Task ReturnsNotFound_ForPendingReservation()
    {
        var id = Guid.NewGuid();
        var pending = Tenant.CreatePendingReservation(id, "new-business", "owner-subject");
        var handler = new GetTenantDatabaseInfoCommandHandler(new FakeTenantReadRepository(pending));

        var result = await handler.ExecuteAsync(new GetTenantDatabaseInfoCommand { TenantId = id.ToString() }, default);

        Assert.False(result.Found);
    }

    /// <summary>A valid GUID matching an existing tenant must yield a found result with the correct strategy and identifier.</summary>
    [Fact]
    public async Task ReturnsStrategy_WhenTenantExists()
    {
        var id = Guid.NewGuid();
        var tenant = Tenant.Create(id, "acme", "shared", "postgres", false);
        var handler = new GetTenantDatabaseInfoCommandHandler(new FakeTenantReadRepository(tenant));

        var result = await handler.ExecuteAsync(
            new GetTenantDatabaseInfoCommand { TenantId = id.ToString(), ServiceName = "order" }, default);

        Assert.True(result.Found);
        Assert.Equal("shared", result.DatabaseStrategy);
        Assert.Equal("acme", result.Identifier);
    }

    [Fact]
    public async Task PaidTenant_ReturnsExclusiveValidityBoundForGatewayCacheEnforcement()
    {
        var tenantId = Guid.NewGuid();
        var paidUntil = DateTimeOffset.UtcNow.AddDays(2);
        var tenant = Tenant.CreatePendingReservation(tenantId, "paid-tenant", "owner");
        tenant.ApplyPurchaseConfirmation(Guid.NewGuid(), Guid.NewGuid(), "Business", "2026-10", "shared", "postgres", false, paidUntil);
        tenant.MarkProvisionerReady("license");
        tenant.MarkProvisionerReady("database");
        tenant.LinkOrganization(Guid.NewGuid());
        var handler = new GetTenantDatabaseInfoCommandHandler(new FakeTenantReadRepository(tenant));

        var result = await handler.ExecuteAsync(new GetTenantDatabaseInfoCommand { TenantId = tenantId.ToString("D"), ServiceName = "order" }, default);

        Assert.True(result.Found);
        Assert.Equal(paidUntil, result.ValidUntilUtc);
    }

}
