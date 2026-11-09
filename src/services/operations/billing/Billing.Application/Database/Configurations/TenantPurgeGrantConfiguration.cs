using Billings.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billings.Application.Database.Configurations;

/// <summary>Maps the durable per-tenant purge fence.</summary>
public sealed class TenantPurgeGrantConfiguration : IEntityTypeConfiguration<TenantPurgeGrant>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<TenantPurgeGrant> builder)
    {
        builder.ToTable("tenant_purge_grants");
        builder.HasKey(grant => grant.Id);
        builder.Property(grant => grant.Id).ValueGeneratedNever();
        builder.Property(grant => grant.TenantId).IsRequired();
        builder.Property(grant => grant.SubscriptionId).IsRequired();
        builder.Property(grant => grant.SourceOrderId).IsRequired();
        builder.Property(grant => grant.ApprovalId).IsRequired();
        builder.Property(grant => grant.AcquiredAtUtc).IsRequired();
        builder.HasIndex(grant => grant.TenantId).IsUnique().HasFilter("\"CompletedAtUtc\" IS NULL");
        builder.HasIndex(grant => grant.ApprovalId).IsUnique();
        builder.HasIndex(grant => grant.SourceOrderId);
    }
}
