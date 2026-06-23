using JasperFx.MultiTenancy;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;

namespace SharedKernel.Infrastructure.Database.EFCore;

public static class TenantDbContextOptionsBuilderExtensions
{
    public static DbContextOptionsBuilder UseTeckCloudTenant(this DbContextOptionsBuilder builder, TenantId tenantId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentNullException.ThrowIfNull(tenantId);

        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new TenantDbContextOptionsExtension(tenantId.Value));

        return builder;
    }

    public static DbContextOptionsBuilder UseTeckCloudTenant(this DbContextOptionsBuilder builder, string tenantId)
    {
        ArgumentNullException.ThrowIfNull(builder);
        ArgumentException.ThrowIfNullOrWhiteSpace(tenantId);

        ((IDbContextOptionsBuilderInfrastructure)builder)
            .AddOrUpdateExtension(new TenantDbContextOptionsExtension(tenantId));

        return builder;
    }

    public static DbContextOptionsBuilder<TContext> UseTeckCloudTenant<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        TenantId tenantId)
        where TContext : DbContext
    {
        UseTeckCloudTenant((DbContextOptionsBuilder)builder, tenantId);
        return builder;
    }

    public static DbContextOptionsBuilder<TContext> UseTeckCloudTenant<TContext>(
        this DbContextOptionsBuilder<TContext> builder,
        string tenantId)
        where TContext : DbContext
    {
        UseTeckCloudTenant((DbContextOptionsBuilder)builder, tenantId);
        return builder;
    }
}
