using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pricing.Domain.Entities;

namespace Pricing.Application.Database.Configurations;

/// <summary>Configures bounded pending catalog fallback reconciliation rows.</summary>
public sealed class PendingPriceResolutionConfiguration : IEntityTypeConfiguration<PendingPriceResolution>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PendingPriceResolution> builder)
    {
        builder.ToTable("PendingPriceResolutions");
        builder.HasKey(resolution => resolution.Id);
        builder.Ignore(resolution => resolution.DomainEvents);
        builder.Property(resolution => resolution.TenantId).HasMaxLength(64);
        builder.Property(resolution => resolution.Currency).HasMaxLength(3);
        builder.Property(resolution => resolution.RequestId).HasMaxLength(128);
        builder.Property(resolution => resolution.SourceCorrelationId).HasMaxLength(128);
        builder.HasIndex(resolution => new { resolution.TenantId, resolution.RequestId }).IsUnique();
    }
}
