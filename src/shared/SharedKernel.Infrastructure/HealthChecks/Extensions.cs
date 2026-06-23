using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using RabbitMQ.Client;

namespace SharedKernel.Infrastructure.HealthChecks;

/// <summary>
/// Shared health check registration helpers for infrastructure dependencies.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Adds PostgreSQL database health checks for write and optional read endpoints.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="writeConnectionString">The write database connection string.</param>
    /// <param name="readConnectionString">The optional read-only database connection string.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddReadWriteHealthChecks(
        this WebApplicationBuilder builder,
        string writeConnectionString,
        string? readConnectionString = null)
    {
        var healthChecks = builder.Services.AddHealthChecks();

        healthChecks.AddNpgSql(writeConnectionString, name: "postgres-write", tags: new[] { "database", "postgres", "write", "ready" });

        if (!string.IsNullOrWhiteSpace(readConnectionString) &&
            !string.Equals(writeConnectionString, readConnectionString, StringComparison.OrdinalIgnoreCase))
        {
            healthChecks.AddNpgSql(readConnectionString, name: "postgres-read", tags: new[] { "database", "postgres", "read", "ready" });
        }

        return builder;
    }

    /// <summary>
    /// Adds a RabbitMQ health check.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="rabbitMqConnectionString">The RabbitMQ connection string.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddRabbitMqHealthCheck(this WebApplicationBuilder builder, string rabbitMqConnectionString)
    {
        builder.Services
            .AddHealthChecks()
            .AddRabbitMQ(
                _ =>
                {
                    var factory = new ConnectionFactory
                    {
                        Uri = new Uri(rabbitMqConnectionString),
                        AutomaticRecoveryEnabled = true,
                    };

                    return factory.CreateConnectionAsync();
                },
                timeout: TimeSpan.FromSeconds(5),
                tags: new[] { "messagebus", "rabbitmq", "ready" });

        return builder;
    }

    /// <summary>
    /// Adds a Redis health check.
    /// </summary>
    /// <param name="builder">The web application builder.</param>
    /// <param name="redisConnectionString">The Redis connection string.</param>
    /// <returns>The same builder for chaining.</returns>
    public static WebApplicationBuilder AddRedisHealthCheck(this WebApplicationBuilder builder, string redisConnectionString)
    {
        builder.Services
            .AddHealthChecks()
            .AddRedis(redisConnectionString, tags: new[] { "cache", "redis", "ready" });

        return builder;
    }
}
