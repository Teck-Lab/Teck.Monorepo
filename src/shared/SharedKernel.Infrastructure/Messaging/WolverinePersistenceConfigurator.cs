using JasperFx;
using JasperFx.CodeGeneration;
using JasperFx.MultiTenancy;
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
    /// Configures common Wolverine runtime setup shared by services, including the RabbitMQ transport.
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
        ConfigureCoreRuntime(options, isDevelopment, writeConnectionString, tenantSource);

        var rabbit = options.UseRabbitMq(new Uri(rabbitConnectionString, UriKind.Absolute));
        rabbit.AutoProvision();
        rabbit.EnableWolverineControlQueues();
        rabbit.UseConventionalRouting();
    }

    /// <summary>
    /// Configures the common Wolverine runtime setup shared by services, without attaching the
    /// RabbitMQ transport. Suitable for standalone development or deployments where no broker
    /// connection string is configured; messages are dispatched through durable local queues only,
    /// so cross-service integration events are not published anywhere.
    /// </summary>
    /// <param name="options">The Wolverine options.</param>
    /// <param name="isDevelopment">Whether the hosting environment is development.</param>
    /// <param name="writeConnectionString">The write database connection string.</param>
    /// <param name="tenantSource">Optional tenant source used for dynamic per-tenant connection resolution.</param>
    public static void ConfigureLocalOnlyRuntime(
        WolverineOptions options,
        bool isDevelopment,
        string writeConnectionString,
        ITenantedSource<string>? tenantSource = null)
    {
        ConfigureCoreRuntime(options, isDevelopment, writeConnectionString, tenantSource);
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
            // Dynamic per-tenant message stores (a future capability): each tenant resolves to its
            // own connection string via the supplied source.
            persistence.RegisterTenants(tenantSource);
        }

        // When no tenant source is supplied the service uses a single, non-partitioned message
        // store. Teck multi-tenancy is row-level (one database per service; ITenantScoped + EF
        // global query filters + envelope TenantId propagation), NOT one database per tenant, so
        // Wolverine's master-table tenancy does not apply. Configuring it with an empty tenant
        // table (the previous `UseMasterTableTenancy(_ => { })`) leaves the tenant-lookup query
        // uninitialized and throws "CommandText property has not been initialized" the moment the
        // message store initializes at startup.
        persistence.OverrideAutoCreateResources(AutoCreate.CreateOrUpdate);
    }

    /// <summary>
    /// Configures the Wolverine runtime setup common to both <see cref="ConfigureStandardRuntime"/>
    /// and <see cref="ConfigureLocalOnlyRuntime"/> (codegen mode, durable database persistence,
    /// serialization, EF Core transactions and durable local queues), excluding any transport.
    /// </summary>
    /// <param name="options">The Wolverine options.</param>
    /// <param name="isDevelopment">Whether the hosting environment is development.</param>
    /// <param name="writeConnectionString">The write database connection string.</param>
    /// <param name="tenantSource">Optional tenant source used for dynamic per-tenant connection resolution.</param>
    private static void ConfigureCoreRuntime(
        WolverineOptions options,
        bool isDevelopment,
        string writeConnectionString,
        ITenantedSource<string>? tenantSource)
    {
        options.CodeGeneration.TypeLoadMode = isDevelopment
            ? TypeLoadMode.Dynamic
            : TypeLoadMode.Static;

        ConfigureDatabasePersistence(options, writeConnectionString, tenantSource);

        // The `wolverine` message-store schema (outbox/inbox/durable-queue tables) is not an EF
        // migration. In production it is created by the migrate init container before the runtime
        // pods start, so the runtime must not attempt DDL at startup (AutoCreate.None) — this keeps
        // runtime database credentials free of schema-owner privileges and avoids startup races
        // across replicas. In local/dev and integration tests there is no init container, so let
        // Wolverine create-or-update the schema on startup. This mirrors the Development gate used
        // for EF migrations in LocalDatabaseMigrationExtensions.ShouldApplyMigrations.
        options.AutoBuildMessageStorageOnStartup = isDevelopment
            ? AutoCreate.CreateOrUpdate
            : AutoCreate.None;
        options.UseMemoryPackSerialization();

        options.UseEntityFrameworkCoreTransactions();
        options.Policies.UseDurableLocalQueues();
        options.Policies.AddMiddleware<TenantPropagationMiddleware>();
    }
}
