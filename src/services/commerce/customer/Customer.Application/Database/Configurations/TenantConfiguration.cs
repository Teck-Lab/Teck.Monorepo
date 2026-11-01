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
        builder.Property(tenant => tenant.OwnerSubjectId).IsRequired().HasMaxLength(128);
        builder.Property(tenant => tenant.PlanId).IsRequired(false).HasMaxLength(64);
        builder.Property(tenant => tenant.PlanVersion).IsRequired(false).HasMaxLength(64);
        builder.Property(tenant => tenant.EnterprisePolicyId).IsRequired(false).HasMaxLength(128);
        builder.Property(tenant => tenant.EnterprisePolicyVersion).IsRequired(false).HasMaxLength(128);
        builder.Property(tenant => tenant.EnterprisePolicyGracePeriodDays).IsRequired(false);
        builder.Property(tenant => tenant.EnterprisePolicyBackupRetentionDays).IsRequired(false);
        builder.Property(tenant => tenant.EnterprisePolicySigningKeyId).IsRequired(false).HasMaxLength(128);
        builder.Property(tenant => tenant.EnterprisePolicySignature).IsRequired(false);
        builder.Property(tenant => tenant.EnterprisePolicyValidFromUtc).IsRequired(false);
        builder.Property(tenant => tenant.EnterprisePolicyValidUntilUtc).IsRequired(false);
        builder.Property(tenant => tenant.EnterprisePolicyTransitionProof).IsRequired(false);
        builder.Property(tenant => tenant.EnterprisePolicySourceOrderId).IsRequired(false);
        builder.HasIndex(tenant => tenant.KeycloakOrganizationId).IsUnique();
    }
}
