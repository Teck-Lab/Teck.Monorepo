using Gateway.Edge;
using Microsoft.Extensions.Logging.Abstractions;
using Polly;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using Xunit;
using ZiggyCreatures.Caching.Fusion;

namespace Gateway.WebAdmin.UnitTests.Edge;

/// <summary>Guards the paid-period authorization boundary even when the gateway has cached Customer metadata.</summary>
public sealed class RemoteTenantDatabaseStrategyResolverTests
{
    [Fact]
    public async Task ExpiredPaidTenant_CachedSuccessCannotReturnDatabaseStrategy()
    {
        using var cache = new FusionCache(new FusionCacheOptions());
        var tenantId = Guid.NewGuid().ToString("D");
        await cache.SetAsync($"tenant-db-strategy:{tenantId}:order", new TenantDatabaseInfoRpcResult
        {
            Found = true,
            TenantId = tenantId,
            DatabaseStrategy = "dedicated",
            ValidUntilUtc = DateTimeOffset.UtcNow.AddSeconds(-1),
        });
        var resolver = new RemoteTenantDatabaseStrategyResolver(cache, ResiliencePipeline.Empty,
            NullLogger<RemoteTenantDatabaseStrategyResolver>.Instance);

        var result = await resolver.ResolveAsync(tenantId, "order", TestContext.Current.CancellationToken);

        Assert.False(result.Success);
        Assert.Null(result.DatabaseStrategy);
    }

    [Fact]
    public async Task PaidTenant_TransitionsFromCachedSuccessToDeniedAtExactValidityEnd()
    {
        using var cache = new FusionCache(new FusionCacheOptions());
        var tenantId = Guid.NewGuid().ToString("D");
        var paidUntil = DateTimeOffset.UtcNow.AddMilliseconds(500);
        await cache.SetAsync($"tenant-db-strategy:{tenantId}:order", new TenantDatabaseInfoRpcResult
        {
            Found = true,
            TenantId = tenantId,
            DatabaseStrategy = "shared",
            ValidUntilUtc = paidUntil,
        });
        var resolver = new RemoteTenantDatabaseStrategyResolver(cache, ResiliencePipeline.Empty,
            NullLogger<RemoteTenantDatabaseStrategyResolver>.Instance);
        var valid = await resolver.ResolveAsync(tenantId, "order", TestContext.Current.CancellationToken);
        Assert.True(valid.Success);
        Assert.Equal("shared", valid.DatabaseStrategy);

        await Task.Delay(paidUntil - DateTimeOffset.UtcNow + TimeSpan.FromMilliseconds(30), TestContext.Current.CancellationToken);
        var expired = await resolver.ResolveAsync(tenantId, "order", TestContext.Current.CancellationToken);
        Assert.False(expired.Success);
        Assert.Equal("tenant.subscription.expired", expired.ErrorCode);
    }

}
