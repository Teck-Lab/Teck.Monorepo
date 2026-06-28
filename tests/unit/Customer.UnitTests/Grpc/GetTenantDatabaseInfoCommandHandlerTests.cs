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
}
