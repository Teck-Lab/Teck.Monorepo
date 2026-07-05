using System.Diagnostics.CodeAnalysis;
using System.Reflection;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.Extensions;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Migrations;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using SharedKernel.Core.Caching;
using SharedKernel.Core.Pricing;
using SharedKernel.Infrastructure.Caching;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Database.EFCore.Interceptors;
using SharedKernel.Infrastructure.HealthChecks;
using SharedKernel.Infrastructure.MultiTenant;

namespace SharedKernel.Infrastructure.Database.MultiTenant;

/// <summary>
/// Extensions for configuring hybrid multi-tenant database support.
/// </summary>
public static class MultiTenantDbExtensions
{
    /// <summary>
    /// Adds hybrid multi-tenant database contexts that support both shared and isolated databases.
    /// </summary>
    /// <remarks>
    /// Use this method for services that need multi-tenancy capabilities (like Site.Api, Device.Api, Catalog.Api).
    /// This approach supports runtime switching between shared database, dedicated databases, or external databases
    /// based on tenant configuration.
    /// For services that don't need multi-tenancy (like Customer.Api), use AddCustomDbContexts from Extensions.cs instead.
    /// </remarks>
    /// <typeparam name="TWriteContext">The write context type.</typeparam>
    /// <typeparam name="TReadContext">The read context type.</typeparam>
    /// <param name="builder">The web application builder.</param>
    /// <param name="migrationsAssembly">The assembly containing migrations.</param>
    /// <param name="defaultWriteConnectionString">The default write connection string for shared database.</param>
    /// <param name="defaultReadConnectionString">The default read connection string for shared database.</param>
    /// <param name="defaultProvider">The default database provider (defaults to PostgreSQL).</param>
    /// <param name="serviceName">
    /// The Vault path segment for this service (e.g. <c>catalog</c>).
    /// Used to locate tenant secrets at <c>teck-cloud/tenants/{tenantId}/{serviceName}</c>.
    /// </param>
    public static void AddHybridMultiTenantDbContexts<[DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TWriteContext, [DynamicallyAccessedMembers(DynamicallyAccessedMemberTypes.PublicConstructors | DynamicallyAccessedMemberTypes.NonPublicConstructors | DynamicallyAccessedMemberTypes.PublicProperties)] TReadContext>(
        this WebApplicationBuilder builder,
        Assembly? migrationsAssembly,
        string defaultWriteConnectionString,
        string defaultReadConnectionString,
        DatabaseProvider? defaultProvider = null,
        string serviceName = "")
        where TWriteContext : BaseDbContext
        where TReadContext : BaseDbContext
    {
        DatabaseProvider effectiveProvider = defaultProvider ?? DatabaseProvider.PostgreSQL;

        // Register Finbuckle multi-tenant infrastructure (resolver + accessor) so the
        // IMultiTenantContextAccessor<TenantDetails> required below is always available, and
        // project a plain ITenantInfo scoped service from it so Application handlers can depend
        // on ITenantInfo directly (see src/services/AGENTS.md "Multi-Tenancy" — "resolved
        // automatically by Finbuckle middleware"). Safe to call even if a host also registers
        // multi-tenancy itself (e.g. integration test harnesses): Finbuckle's registrations are
        // additive, and the last registration wins when resolved.
        builder.Services.AddMultiTenant<TenantDetails>();
        builder.Services.AddScoped<ITenantInfo>(sp =>
            sp.GetRequiredService<IMultiTenantContextAccessor<TenantDetails>>().MultiTenantContext?.TenantInfo
                ?? new TenantDetails());

        // Bind OpenBao options and register the vault connection provider
        var openBaoOptions = builder.Configuration
            .GetSection(OpenBaoOptions.Section)
            .Get<OpenBaoOptions>() ?? new OpenBaoOptions();

        builder.Services.AddSingleton<IVaultTenantConnectionProvider>(sp =>
        {
            if (string.IsNullOrWhiteSpace(openBaoOptions.Url))
            {
                return new NullVaultTenantConnectionProvider();
            }

            var logger = sp.GetRequiredService<ILogger<VaultTenantConnectionProvider>>();
            return new VaultTenantConnectionProvider(openBaoOptions, serviceName, logger);
        });

        builder.Services.AddScoped(typeof(IGenericCacheService<,>), typeof(GenericCacheService<,>));

        // Register the tenant database resolver service
        builder.Services.AddScoped<ITenantDbConnectionResolver>(sp =>
        {
            var vaultProvider = sp.GetRequiredService<IVaultTenantConnectionProvider>();
            return new TenantDbConnectionResolver(
                sp,
                defaultWriteConnectionString,
                defaultReadConnectionString,
                effectiveProvider,
                vaultProvider,
                openBaoOptions.DefaultStrategy);
        });
        builder.Services.AddScoped<AuditingInterceptor>();
        builder.Services.AddScoped<SoftDeleteInterceptor>();

        // Register the custom provider for tenant-aware DbContext access
        builder.Services.AddScoped(typeof(ICurrentTenantDbContext<>), typeof(CurrentTenantDbContext<>));

        // Register HTTP client for Customer.Api
        builder.Services.AddHttpClient("CustomerApi", client =>
        {
            // Configure base address from configuration if available
            var customerApiUrl = builder.Configuration["Services:CustomerApi:Url"];
            if (!string.IsNullOrEmpty(customerApiUrl))
            {
                client.BaseAddress = new Uri(customerApiUrl);
            }

            // Add default headers, timeout, etc.
            client.Timeout = TimeSpan.FromSeconds(5);
        });

        // Register factories for IDbContextFactory<T> consumers (e.g. repositories using CreateDbContextAsync)
        builder.Services.AddDbContextFactory<TWriteContext>((serviceProvider, options) =>
        {
            ConfigureTenantDbContext(
                options,
                null,
                defaultWriteConnectionString,
                migrationsAssembly,
                effectiveProvider,
                DatabaseStrategy.Shared,
                false);
        });

        builder.Services.AddDbContextFactory<TReadContext>((serviceProvider, options) =>
        {
            // Configure the factory to use basic DbContext options - tenant resolution happens at runtime
            ConfigureTenantDbContext(
                options,
                null,
                defaultReadConnectionString,
                migrationsAssembly: null,
                effectiveProvider,
                DatabaseStrategy.Shared,
                true);
        });

        // Runtime-tenant-aware write context registration
        builder.Services.AddScoped<TWriteContext>(sp =>
        {
            var tenantAccessor = sp.GetRequiredService<IMultiTenantContextAccessor<TenantDetails>>();
            var tenantInfo = tenantAccessor.MultiTenantContext?.TenantInfo;
            var connectionResolver = sp.GetRequiredService<ITenantDbConnectionResolver>();
            (string WriteConnectionString, string? ReadConnectionString, DatabaseProvider Provider, DatabaseStrategy Strategy) resolved;
            if (tenantInfo != null)
            {
                resolved = connectionResolver.ResolveTenantConnection(tenantInfo);
            }
            else
            {
                resolved = (defaultWriteConnectionString, defaultReadConnectionString, effectiveProvider, DatabaseStrategy.Shared);
            }

            var optionsBuilder = new DbContextOptionsBuilder<TWriteContext>();
            DatabaseProvider effectiveResolvedProvider = resolved.Provider ?? effectiveProvider;
            DatabaseStrategy effectiveResolvedStrategy = resolved.Strategy ?? DatabaseStrategy.Shared;
            ConfigureTenantDbContext(
                optionsBuilder,
                sp,
                resolved.WriteConnectionString,
                migrationsAssembly,
                effectiveResolvedProvider,
                effectiveResolvedStrategy,
                isReadOnly: false);

            // Use single constructor - tenant info is embedded in the options
            return ActivatorUtilities.CreateInstance<TWriteContext>(sp, optionsBuilder.Options);
        });

        // Runtime-tenant-aware read context registration
        builder.Services.AddScoped<TReadContext>(sp =>
        {
            var tenantAccessor = sp.GetRequiredService<IMultiTenantContextAccessor<TenantDetails>>();
            var tenantInfo = tenantAccessor.MultiTenantContext?.TenantInfo;
            var connectionResolver = sp.GetRequiredService<ITenantDbConnectionResolver>();
            (string WriteConnectionString, string? ReadConnectionString, DatabaseProvider Provider, DatabaseStrategy Strategy) resolved;
            if (tenantInfo != null)
            {
                resolved = connectionResolver.ResolveTenantConnection(tenantInfo);
            }
            else
            {
                resolved = (defaultWriteConnectionString, defaultReadConnectionString, effectiveProvider, DatabaseStrategy.Shared);
            }

            var optionsBuilder = new DbContextOptionsBuilder<TReadContext>();
            DatabaseProvider effectiveResolvedProvider = resolved.Provider ?? effectiveProvider;
            DatabaseStrategy effectiveResolvedStrategy = resolved.Strategy ?? DatabaseStrategy.Shared;
            ConfigureTenantDbContext(
                optionsBuilder,
                sp,
                resolved.ReadConnectionString ?? resolved.WriteConnectionString,
                migrationsAssembly: null,
                effectiveResolvedProvider,
                effectiveResolvedStrategy,
                isReadOnly: true);

            // Use single constructor - tenant info is embedded in the options
            return ActivatorUtilities.CreateInstance<TReadContext>(sp, optionsBuilder.Options);
        });

        // Do NOT register TWriteContext or TReadContext directly for DI to avoid accidental direct injection and constructor errors
        // Only register the factories and context interfaces as needed

        // Add provider-specific read/write database health checks for readiness
        builder.AddReadWriteHealthChecks(defaultWriteConnectionString, defaultReadConnectionString);
    }

    /// <summary>
    /// Configures a tenant-specific database context.
    /// </summary>
    private static void ConfigureTenantDbContext(
        DbContextOptionsBuilder options,
        IServiceProvider? serviceProvider,
        string connectionString,
        Assembly? migrationsAssembly,
        DatabaseProvider provider,
        DatabaseStrategy strategy,
        bool isReadOnly)
    {
        provider ??= DatabaseProvider.PostgreSQL;
        strategy ??= DatabaseStrategy.Shared;

        // Configure provider-specific options using SmartEnum Name
        if (migrationsAssembly != null)
        {
            options.UseNpgsql(connectionString, npgsql =>
                npgsql.MigrationsAssembly(migrationsAssembly.FullName));
        }
        else
        {
            options.UseNpgsql(connectionString);
        }

        if (migrationsAssembly != null)
        {
            options.ReplaceService<IMigrationsAssembly, ProviderFilteredMigrationsAssembly>();
        }

        // Add required interceptors only when we have a valid service provider
        // (not during factory registration time)
        if (serviceProvider != null)
        {
            if (isReadOnly)
            {
                // For read-only context, add only auditing
                var auditingInterceptor = serviceProvider.GetRequiredService<AuditingInterceptor>();
                options.AddInterceptors(auditingInterceptor);

                // Optimize for reading
                options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            }
            else
            {
                // For write context, add all interceptors
                var softDeleteInterceptor = serviceProvider.GetRequiredService<SoftDeleteInterceptor>();
                var auditingInterceptor = serviceProvider.GetRequiredService<AuditingInterceptor>();

                options.AddInterceptors(
                    softDeleteInterceptor,
                    auditingInterceptor);
            }
        }
        else
        {
            // During factory registration, just configure basic query behavior
            if (isReadOnly)
            {
                options.UseQueryTrackingBehavior(QueryTrackingBehavior.NoTracking);
            }
        }

        // For shared database strategy, configure Finbuckle multi-tenancy
        // This is only needed for the shared database option
        if (strategy == DatabaseStrategy.Shared)
        {
            // The multi-tenant configuration is applied in the DbContext's OnModelCreating method
            // instead of here, using modelBuilder.ConfigureMultiTenant()
        }
    }
}
