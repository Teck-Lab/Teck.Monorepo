using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using SharedKernel.Core.Caching;
using SharedKernel.Core.Pricing;
using SharedKernel.Infrastructure.Caching;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.EFCore.Interceptors;
using SharedKernel.Infrastructure.Messaging.MultiTenant;

namespace SharedKernel.Infrastructure.Database;

/// <summary>
/// Database extensions for registering EF Core contexts and related services.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Adds both read and write database contexts to the application with the specified provider.
    /// </summary>
    /// <remarks>
    /// Use this method for services that don't need multi-tenancy (like Customer.Api).
    /// For multi-tenant services, use AddHybridMultiTenantDbContexts from MultiTenantDbExtensions instead.
    /// </remarks>
    /// <typeparam name="TWriteContext">The write context type (for commands).</typeparam>
    /// <typeparam name="TReadContext">The read context type (for queries).</typeparam>
    /// <param name="builder">The web application builder.</param>
    /// <param name="assembly">The assembly containing migrations.</param>
    /// <param name="defaultWriteConnectionString">The default write connection string for the shared database.</param>
    /// <param name="defaultReadConnectionString">The default read connection string for the shared database.</param>
    /// <param name="provider">The database provider to use (defaults to PostgreSQL).</param>
    public static void AddCustomDbContexts<
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TWriteContext,
        [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TReadContext>(
        this WebApplicationBuilder builder,
        Assembly assembly,
        string defaultWriteConnectionString,
        string defaultReadConnectionString,
        DatabaseProvider provider)
        where TWriteContext : BaseDbContext
        where TReadContext : BaseDbContext
    {
        builder.Services.AddScoped(typeof(IGenericCacheService<,>), typeof(GenericCacheService<,>));

        // Defensive: Prevent accidental registration of multi-tenant contexts
        var forbidden = new[] { "ApplicationWriteDbContext", "ApplicationReadDbContext" };
        if (forbidden.Contains(typeof(TWriteContext).Name) || forbidden.Contains(typeof(TReadContext).Name))
        {
            throw new InvalidOperationException($"Do not use AddCustomDbContexts for multi-tenant contexts like {typeof(TWriteContext).Name} or {typeof(TReadContext).Name}. Use AddHybridMultiTenantDbContexts instead.");
        }

        // Add write context with all interceptors
        AddWriteDbContext<TWriteContext>(builder, assembly, defaultWriteConnectionString, provider);

        // Add read context with minimal interceptors
        AddReadDbContext<TReadContext>(builder, defaultReadConnectionString, provider);

        // Add health check for the appropriate provider
        AddDbHealthCheck(builder, defaultWriteConnectionString, defaultReadConnectionString, provider);
    }

    /// <summary>
    /// Configures database context options based on the selected provider.
    /// </summary>
    /// <param name="options">The DbContext options builder.</param>
    /// <param name="connectionString">The database connection string.</param>
    /// <param name="migrationsAssembly">Optional migrations assembly.</param>
    /// <param name="provider">The selected database provider.</param>
    public static void ConfigureProviderDbContextOptions(
        DbContextOptionsBuilder options,
        string connectionString,
        Assembly? migrationsAssembly,
        DatabaseProvider provider)
    {
        ConfigureDbContextOptions(options, connectionString, migrationsAssembly, provider);

        if (!string.IsNullOrWhiteSpace(TenantPropagationContext.CurrentTenantId))
        {
            options.UseTeckCloudTenant(TenantPropagationContext.CurrentTenantId!);
        }
    }

    /// <summary>
    /// Gets the database provider from configuration.
    /// </summary>
    /// <param name="configuration">The configuration.</param>
    /// <returns>The database provider.</returns>
    public static DatabaseProvider GetDatabaseProvider(this IConfiguration configuration)
    {
        var providerName = configuration["Database:Provider"]
            ?? configuration["Database__Provider"]
            ?? "PostgreSQL";

        return DatabaseProvider.PostgreSQL;
    }

    /// <summary>
    /// Adds a write database context with all required interceptors.
    /// </summary>
    private static void AddWriteDbContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TContext>(
        WebApplicationBuilder builder,
        Assembly assembly,
        string connectionString,
        DatabaseProvider provider)
        where TContext : BaseDbContext
    {
        // Defensive: Prevent accidental registration of multi-tenant contexts
        var forbidden = new[] { "ApplicationWriteDbContext", "ApplicationReadDbContext" };
        if (forbidden.Contains(typeof(TContext).Name))
        {
            throw new InvalidOperationException($"Do not use AddWriteDbContext for multi-tenant context {typeof(TContext).Name}. Use AddHybridMultiTenantDbContexts instead.");
        }

        builder.Services.AddScoped<SoftDeleteInterceptor>();
        builder.Services.AddScoped<AuditingInterceptor>();

        builder.Services.AddDbContext<TContext>((sp, options) =>
        {
            ConfigureDbContextOptions(options, connectionString, assembly, provider);

            options.AddInterceptors(
                sp.GetRequiredService<SoftDeleteInterceptor>(),
                sp.GetRequiredService<AuditingInterceptor>());
        });

        // Enrich the context based on the provider
        EnrichDbContext<TContext>(builder, provider);
        builder.Services.AddScoped<TContext>();
        builder.Services.AddScoped<IBaseDbContext>(sp => (IBaseDbContext)sp.GetRequiredService<TContext>());
    }

    /// <summary>
    /// Adds a read-only database context with minimal interceptors.
    /// </summary>
    private static void AddReadDbContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TContext>(
        WebApplicationBuilder builder,
        string connectionString,
        DatabaseProvider provider)
        where TContext : BaseDbContext
    {
        // Defensive: Prevent accidental registration of multi-tenant contexts
        var forbidden = new[] { "ApplicationWriteDbContext", "ApplicationReadDbContext" };
        if (forbidden.Contains(typeof(TContext).Name))
        {
            throw new InvalidOperationException($"Do not use AddReadDbContext for multi-tenant context {typeof(TContext).Name}. Use AddHybridMultiTenantDbContexts instead.");
        }

        builder.Services.AddScoped<AuditingInterceptor>();

        builder.Services.AddDbContext<TContext>((sp, options) =>
        {
            ConfigureDbContextOptions(options, connectionString, migrationsAssembly: null, provider);

            options.AddInterceptors(
                sp.GetRequiredService<AuditingInterceptor>());

            // Enable read-optimized features in EF Core
            options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
        });

        // Enrich the context based on the provider
        EnrichDbContext<TContext>(builder, provider);
        builder.Services.AddScoped<TContext>();

        // Register as IBaseDbContext so it can be used for read operations
        builder.Services.AddScoped<IBaseDbContext>(sp => (IBaseDbContext)sp.GetRequiredService<TContext>());
    }

    /// <summary>
    /// Configures database context options based on the selected provider.
    /// </summary>
    private static void ConfigureDbContextOptions(
        DbContextOptionsBuilder options,
        string connectionString,
        Assembly? migrationsAssembly,
        DatabaseProvider provider)
    {
        if (provider == DatabaseProvider.PostgreSQL)
        {
            if (migrationsAssembly != null)
            {
                options.UseNpgsql(connectionString, npgsql =>
                    npgsql.MigrationsAssembly(migrationsAssembly.FullName));
            }
            else
            {
                options.UseNpgsql(connectionString);
            }
        }
        else
        {
            throw new ArgumentException($"Unsupported database provider: {provider}");
        }

        if (migrationsAssembly != null)
        {
            options.ReplaceService<IMigrationsAssembly, ProviderFilteredMigrationsAssembly>();
        }
    }

    /// <summary>
    /// Enriches the DbContext with provider-specific settings.
    /// </summary>
    private static void EnrichDbContext<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TContext>(
        WebApplicationBuilder builder,
        DatabaseProvider provider)
        where TContext : BaseDbContext
    {
        if (provider == DatabaseProvider.PostgreSQL)
        {
            builder.EnrichNpgsqlDbContext<TContext>();
        }
        else
        {
            throw new ArgumentException($"Unsupported database provider: {provider}");
        }
    }

    /// <summary>
    /// Adds appropriate health checks based on the database provider.
    /// </summary>
    private static void AddDbHealthCheck(
        WebApplicationBuilder builder,
        string defaultWriteConnectionString,
        string defaultReadConnectionString,
        DatabaseProvider provider)
    {
        var healthChecks = builder.Services.AddHealthChecks();

        AddProviderDbHealthCheck(healthChecks, defaultWriteConnectionString, provider, role: "write");

        if (!string.Equals(defaultWriteConnectionString, defaultReadConnectionString, StringComparison.OrdinalIgnoreCase))
        {
            AddProviderDbHealthCheck(healthChecks, defaultReadConnectionString, provider, role: "read");
        }
    }

    private static void AddProviderDbHealthCheck(
        IHealthChecksBuilder healthChecks,
        string connectionString,
        DatabaseProvider provider,
        string role)
    {
        if (provider == DatabaseProvider.PostgreSQL)
        {
            healthChecks.AddNpgSql(
                connectionString: connectionString,
                name: $"postgres-{role}",
                tags: ["database", "postgres", role, "ready"]);
        }
        else
        {
            throw new ArgumentException($"Unsupported database provider: {provider}");
        }
    }
}
