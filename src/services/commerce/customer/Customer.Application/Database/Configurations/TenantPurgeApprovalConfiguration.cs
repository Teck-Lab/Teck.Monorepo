using Customers.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Customers.Application.Database.Configurations;

/// <summary>Maps global, order-bound purge approvals without a tenant-scoped query filter.</summary>
public sealed class TenantPurgeApprovalConfiguration : IEntityTypeConfiguration<TenantPurgeApproval>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TenantPurgeApproval> builder)
    {
        builder.ToTable("tenant_purge_approvals");
        builder.HasKey(row => row.Id);
        builder.Property(row => row.ApproverSubjectId).IsRequired().HasMaxLength(128);
        builder.HasIndex(row => new { row.TenantId, row.SourceOrderId, row.ApproverSubjectId }).IsUnique();
        builder.HasIndex(row => new { row.TenantId, row.SourceOrderId });
    }
}
