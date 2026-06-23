using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.MultiTenancy;
using SharedKernel.Core.Domain;
using SharedKernel.Infrastructure.Messaging.MultiTenant;
using Wolverine;
using Wolverine.EntityFrameworkCore;
using Wolverine.MemoryPack;
using Wolverine.Postgresql;
using Wolverine.RabbitMQ;

namespace SharedKernel.Infrastructure.Messaging;

/// <summary>
/// Configures Wolverine durable persistence for PostgreSQL.
/// </summary>
public static class WolverinePersistenceConfigurator
{
    private const string WolverineSchemaName = "wolverine";

    /// <summary>
    /// Normalizes RabbitMQ connection-string schemes to AMQP-compatible URIs.
    /// </summary>
    /// <param name="rabbitConnectionString">The configured RabbitMQ connection string.</param>
    /// <returns>A normalized AMQP/AMQPS URI string.</returns>
    public static string NormalizeRabbitConnectionString(string rabbitConnectionString)
    {
        if (rabbitConnectionString.StartsWith("rabbitmqs://", StringComparison.OrdinalIgnoreCase))
        {
            return "amqps://" + rabbitConnectionString["rabbitmqs://".Length..];
        }

        if (rabbitConnectionString.StartsWith("rabbitmq://", StringComparison.OrdinalIgnoreCase))
        {
            return "amqp://" + rabbitConnectionString["rabbitmq://".Length..];
        }

        return rabbitConnectionString.Trim();
    }

    /// <summary>
    /// Configures common Wolverine runtime setup shared by services.
    /// </summary>
    /// <param name="options">The Wolverine options.</param>
    /// <param name="isDevelopment">Whether the hosting environment is development.</param>
    /// <param name="writeConnectionString">The write database connection string.</param>
    /// <param name="rabbitConnectionString">The normalized RabbitMQ connection string.</param>
    /// <param name="tenantSource">Optional tenant source used for dynamic per-tenant connection resolution.</param>
    public static void ConfigureStandardRuntime(
        WolverineOptions options,
        bool isDevelopment,
        string writeConnectionString,
        string rabbitConnectionString,
        ITenantedSource<string>? tenantSource = null)
    {
        options.CodeGeneration.TypeLoadMode = isDevelopment
            ? TypeLoadMode.Dynamic
            : TypeLoadMode.Static;

        ConfigureDatabasePersistence(options, writeConnectionString, tenantSource);
        options.AutoBuildMessageStorageOnStartup = AutoCreate.None;
        options.UseMemoryPackSerialization();

        options.UseEntityFrameworkCoreTransactions();
        options.PublishDomainEventsFromEntityFrameworkCore<BaseEntity>(entity => entity.DomainEvents);
        options.Policies.UseDurableLocalQueues();
        options.Policies.AddMiddleware<TenantPropagationMiddleware>();

        var rabbit = options.UseRabbitMq(new Uri(rabbitConnectionString, UriKind.Absolute));
        rabbit.AutoProvision();
        rabbit.EnableWolverineControlQueues();
        rabbit.UseConventionalRouting();
    }

    /// <summary>
    /// Configures a stateless Wolverine runtime (no durable persistence) with RabbitMQ transport only.
    /// Suitable for stateless services such as image-generator, statistic, and worker processes that do not
    /// require outbox or saga persistence.
    /// </summary>
    /// <param name="options">The Wolverine options.</param>
    /// <param name="isDevelopment">Whether the hosting environment is development.</param>
    /// <param name="rabbitConnectionString">The normalized RabbitMQ connection string.</param>
    public static void ConfigureStatelessRuntime(
        WolverineOptions options,
        bool isDevelopment,
        string rabbitConnectionString)
    {
        options.CodeGeneration.TypeLoadMode = isDevelopment
            ? TypeLoadMode.Dynamic
            : TypeLoadMode.Static;

        options.UseMemoryPackSerialization();

        var rabbit = options.UseRabbitMq(new Uri(rabbitConnectionString, UriKind.Absolute));
        rabbit.AutoProvision();
        rabbit.EnableWolverineControlQueues();
        rabbit.UseConventionalRouting();
    }

    /// <summary>
    /// Configures Wolverine message persistence with PostgreSQL.
    /// </summary>
    /// <param name="options">The Wolverine options.</param>
    /// <param name="writeConnectionString">The write connection string.</param>
    /// <param name="tenantSource">Optional tenant source used for dynamic per-tenant connection resolution.</param>
    public static void ConfigureDatabasePersistence(
        WolverineOptions options,
        string writeConnectionString,
        ITenantedSource<string>? tenantSource = null)
    {
        var persistence = options.PersistMessagesWithPostgresql(writeConnectionString, WolverineSchemaName);
        if (tenantSource is not null)
        {
            persistence.RegisterTenants(tenantSource);
        }
        else
        {
            persistence.UseMasterTableTenancy(static _ => { });
        }

        persistence.OverrideAutoCreateResources(AutoCreate.CreateOrUpdate);
    }
}
