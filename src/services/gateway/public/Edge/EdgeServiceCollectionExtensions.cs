using Gateway.Public.Edge.Steps;
using Polly;
using Polly.CircuitBreaker;
using SharedKernel.Infrastructure.Auth;
using SharedKernel.Infrastructure.MultiTenant;
using ZiggyCreatures.Caching.Fusion;

namespace Gateway.Public.Edge;

/// <summary>Registers edge pipeline services.</summary>
public static class EdgeServiceCollectionExtensions
{
    private const string TenantDbStrategyPipelineKey = "tenant-db-strategy";

    /// <summary>Adds the edge enforcement services to the container.</summary>
    /// <param name="builder">The web application builder.</param>
    /// <returns>The same builder.</returns>
    public static WebApplicationBuilder AddEdgePipeline(this WebApplicationBuilder builder)
    {
        EdgeTenantOptions tenantOptions = builder.Configuration.GetEdgeTenantOptions();
        builder.Services.AddSingleton(tenantOptions);

        // Fail-closed: throws at startup if any non-anonymous route lacks an exchange audience.
        builder.Services.AddSingleton<IEdgeAccessPolicyRegistry>(
            EdgeAccessPolicyRegistry.Build(builder.Configuration));

        // FusionCache with a 1-hour fail-safe cap so stale DB-strategy entries are not
        // served indefinitely (the library default is 1 day).
        builder.Services
            .AddFusionCache()
            .WithDefaultEntryOptions(new FusionCacheEntryOptions()
                .SetDuration(TimeSpan.FromMinutes(5))
                .SetFailSafe(true, TimeSpan.FromHours(1)));

        builder.Services.AddHttpClient("KeycloakTokenClient");
        builder.Services.AddSingleton<IServiceTokenExchangeService, ServiceTokenExchangeService>();
        builder.Services.AddSingleton<ITenantTokenContextResolver, TenantTokenContextResolver>();

        // Keyed circuit-breaker pipeline so the resolver does not depend on a raw
        // non-keyed ResiliencePipeline singleton (which would conflict with any other pipeline).
        builder.Services.AddKeyedSingleton<ResiliencePipeline>(
            TenantDbStrategyPipelineKey,
            (_, _) => new ResiliencePipelineBuilder()
                .AddCircuitBreaker(new CircuitBreakerStrategyOptions
                {
                    FailureRatio = 1.0,
                    MinimumThroughput = 5,
                    SamplingDuration = TimeSpan.FromSeconds(30),
                    BreakDuration = TimeSpan.FromSeconds(30),
                })
                .Build());

        builder.Services.AddSingleton<ITenantDatabaseStrategyResolver, RemoteTenantDatabaseStrategyResolver>();

        // Scoped steps — DI resolves IEnumerable<IEdgeStep> in registration order.
        builder.Services.AddScoped<IEdgeStep, HeaderFirewallStep>();
        builder.Services.AddScoped<IEdgeStep, ResolveTenantStep>();
        builder.Services.AddScoped<IEdgeStep, ResolveDbStrategyStep>();
        builder.Services.AddScoped<IEdgeStep, ExchangeTokenStep>();

        return builder;
    }
}
