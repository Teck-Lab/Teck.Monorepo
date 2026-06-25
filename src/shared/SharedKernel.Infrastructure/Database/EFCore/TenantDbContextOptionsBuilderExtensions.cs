using JasperFx.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SharedKernel.Infrastructure.Database.EFCore;

/// <summary>
/// Provides extension methods for configuring a <see cref="DbContextOptionsBuilder"/> with Teck cloud tenant information.
/// </summary>
public static class TenantDbContextOptionsBuilderExtensions
{
    /// <summary>
    /// Configures the options builder with the specified tenant identifier.
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The configured options builder.</returns>
    public static DbContextOptionsBuilder UseTeckCloudTenant(this DbContextOptionsBuilder builder, TenantId tenantId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tenantId);

        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new TenantDbContextOptionsExtension(tenantId.Value));

        return builder;
    }

    /// <summary>
    /// Configures the options builder with the specified tenant identifier.
    /// </summary>
    /// <param name="builder">The options builder to configure.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The configured options builder.</returns>
    public static DbContextOptionsBuilder UseTeckCloudTenant(this DbContextOptionsBuilder builder, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new TenantDbContextOptionsExtension(tenantId));

        return builder;
    }

    /// <summary>
    /// Configures the typed options builder with the specified tenant identifier.
    /// </summary>
    /// <typeparam name="TContext">The database context type.</typeparam>
    /// <param name="builder">The typed options builder to configure.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The configured typed options builder.</returns>
    public static DbContextOptionsBuilder<TContext> UseTeckCloudTenant<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        TenantId tenantId)
        where TContext : DbContext
    {
        UseTeckCloudTenant((DbContextOptionsBuilder)builder, tenantId);
        return builder;
    }

    /// <summary>
    /// Configures the typed options builder with the specified tenant identifier.
    /// </summary>
    /// <typeparam name="TContext">The database context type.</typeparam>
    /// <param name="builder">The typed options builder to configure.</param>
    /// <param name="tenantId">The tenant identifier.</param>
    /// <returns>The configured typed options builder.</returns>
    public static DbContextOptionsBuilder<TContext> UseTeckCloudTenant<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string tenantId)
        where TContext : DbContext
    {
        UseTeckCloudTenant((DbContextOptionsBuilder)builder, tenantId);
        return builder;
    }
}
