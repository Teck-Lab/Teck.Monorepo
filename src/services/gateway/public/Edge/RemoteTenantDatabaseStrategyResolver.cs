using FastEndpoints;
using Grpc.Core;
using Polly;
using SharedKernel.Grpc.Contracts.Remote.V1.Tenants;
using ZiggyCreatures.Caching.Fusion;

namespace Gateway.Public.Edge;

/// <summary>
/// Resolves a tenant's database strategy via the customer service gRPC remote call,
/// backed by a FusionCache fail-safe layer and a Polly circuit breaker for resilience.
/// </summary>
internal sealed class RemoteTenantDatabaseStrategyResolver : ITenantDatabaseStrategyResolver
{
    private readonly IFusionCache fusionCache;
    private readonly ResiliencePipeline circuitBreaker;
    private readonly ILogger<RemoteTenantDatabaseStrategyResolver> logger;

    /// <summary>Initializes a new instance of the <see cref="RemoteTenantDatabaseStrategyResolver"/> class.</summary>
    /// <param name="fusionCache">The fusion cache instance for fail-safe stale serving.</param>
    /// <param name="circuitBreaker">The Polly resilience pipeline (circuit breaker) wrapping the remote call.</param>
    /// <param name="logger">The logger.</param>
    public RemoteTenantDatabaseStrategyResolver(
        IFusionCache fusionCache,
        ResiliencePipeline circuitBreaker,
        ILogger<RemoteTenantDatabaseStrategyResolver> logger)
    {
        this.fusionCache = fusionCache;
        this.circuitBreaker = circuitBreaker;
        this.logger = logger;
    }

    /// <inheritdoc/>
    public async Task<TenantDbStrategyResult> ResolveAsync(string tenantId, string? serviceName, CancellationToken ct)
    {
        string cacheKey = $"tenant-db-strategy:{tenantId}:{serviceName ?? string.Empty}";

        TenantDatabaseInfoRpcResult reply;
        try
        {
            reply = await fusionCache.GetOrSetAsync<TenantDatabaseInfoRpcResult>(
                cacheKey,
                async ct2 => await circuitBreaker.ExecuteAsync(
                    async innerCt => await new GetTenantDatabaseInfoCommand
                    {
                        TenantId = tenantId,
                        ServiceName = serviceName ?? string.Empty,
                    }.RemoteExecuteAsync(new CallOptions(cancellationToken: innerCt)),
                    ct2),
                opts => opts.SetDuration(TimeSpan.FromMinutes(5)).SetFailSafe(true),
                ct)
                .ConfigureAwait(false);
        }
        catch (RpcException ex)
        {
            logger.LogWarning(
                ex,
                "gRPC error resolving tenant database strategy for tenant {TenantId} / service {ServiceName}",
                tenantId,
                serviceName);
            return TenantDbStrategyResult.Fail(503, "tenant.lookup.unavailable", "The tenant lookup service is temporarily unavailable.");
        }
        catch (Exception ex)
        {
            logger.LogError(
                ex,
                "Unexpected error resolving tenant database strategy for tenant {TenantId} / service {ServiceName}",
                tenantId,
                serviceName);
            return TenantDbStrategyResult.Fail(503, "tenant.lookup.unavailable", "An unexpected error occurred resolving the tenant database strategy.");
        }

        if (!reply.Found)
        {
            string detail = reply.ErrorDetail ?? "Tenant not found.";
            return detail.Contains("not found", StringComparison.OrdinalIgnoreCase)
                ? TenantDbStrategyResult.Fail(404, "tenant.not_found", detail)
                : TenantDbStrategyResult.Fail(400, "tenant.not_found", detail);
        }

        if (string.IsNullOrWhiteSpace(reply.DatabaseStrategy))
        {
            return TenantDbStrategyResult.Fail(503, "tenant.lookup.unavailable", "The customer service returned no database strategy for this tenant.");
        }

        return TenantDbStrategyResult.Ok(reply.DatabaseStrategy);
    }
}
