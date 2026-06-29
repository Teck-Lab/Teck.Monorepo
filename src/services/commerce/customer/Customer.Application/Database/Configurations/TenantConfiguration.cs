using Customers.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Customers.Application.Database.Configurations;

/// <summary>EF Core configuration for the <see cref="Tenant"/> registry.</summary>
public sealed class TenantConfiguration : IEntityTypeConfiguration<Tenant>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Tenant> builder)
    {
        builder.ToTable("tenants");
        builder.HasKey(tenant => tenant.Id);
        builder.Property(tenant => tenant.Identifier).IsRequired().HasMaxLength(128);
        builder.HasIndex(tenant => tenant.Identifier).IsUnique();
        builder.Property(tenant => tenant.DatabaseStrategy).IsRequired().HasMaxLength(64);
        builder.Property(tenant => tenant.DatabaseProvider).IsRequired().HasMaxLength(64);
        builder.Property(tenant => tenant.Status).IsRequired().HasMaxLength(32);
    }
}
