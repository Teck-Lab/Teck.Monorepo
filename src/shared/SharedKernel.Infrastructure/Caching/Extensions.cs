using Medallion.Threading;
using Medallion.Threading.Redis;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using OpenTelemetry.Trace;
using SharedKernel.Core.Exceptions;
using SharedKernel.Infrastructure.HealthChecks;
using StackExchange.Redis;
using ZiggyCreatures.Caching.Fusion;
using ZiggyCreatures.Caching.Fusion.Backplane.StackExchangeRedis;

namespace SharedKernel.Infrastructure.Caching
{
    /// <summary>
    /// The extensions.
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Add caching service.
        /// </summary>
        /// <param name="builder">The builder.</param>
        public static void AddCachingInfrastructure(this WebApplicationBuilder builder)
        {
            var connectionString = builder.Configuration.GetConnectionString("redis")
                ?? throw new ConfigurationMissingException("Redis");

            var redisConnection = ConnectionMultiplexer.Connect(connectionString);

            builder.AddRedisDistributedCache("redis");

            builder.Services
                .AddFusionCache()
                .WithRegisteredDistributedCache()
                .WithBackplane(new RedisBackplane(new RedisBackplaneOptions { Configuration = connectionString }))
                .WithCysharpMemoryPackSerializer()
                .WithDefaultEntryOptions(new FusionCacheEntryOptions()
                    .SetDuration(TimeSpan.FromMinutes(2))
                    .SetPriority(CacheItemPriority.High)
                    .SetFailSafe(true, TimeSpan.FromHours(2))
                    .SetFactoryTimeouts(TimeSpan.FromMilliseconds(100), TimeSpan.FromSeconds(2)));

            builder.Services.AddSingleton<IConnectionMultiplexer>(redisConnection);
            builder.Services.AddSingleton(redisConnection);
            builder.Services.AddSingleton<IDatabase>(serviceProvider => serviceProvider.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

            builder.Services.AddSingleton<IDistributedLockProvider>(serviceProvider =>
            {
                var sharedRedisConnection = serviceProvider.GetRequiredService<IConnectionMultiplexer>();
                return new RedisDistributedSynchronizationProvider(sharedRedisConnection.GetDatabase());
            });

            builder.Services.AddOpenTelemetry()
                .WithTracing(tracing => tracing.AddRedisInstrumentation(redisConnection));

            builder.Services.AddFusionCacheCysharpMemoryPackSerializer();

            builder.AddCachingHealthChecks(connectionString);
        }

        private static void AddCachingHealthChecks(this WebApplicationBuilder builder, string connectionString)
        {
            builder.AddRedisHealthCheck(connectionString);
        }
    }
}
