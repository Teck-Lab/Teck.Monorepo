using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pricing.Domain.Entities;

namespace Pricing.Application.Database.Configurations;

/// <summary>Configures the catalog fallback price projection.</summary>
public sealed class CatalogPriceConfiguration : IEntityTypeConfiguration<CatalogPrice>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<CatalogPrice> builder)
    {
        builder.ToTable("CatalogPrices");
        builder.HasKey(price => price.Id);
        builder.Ignore(price => price.DomainEvents);
        builder.Property(price => price.TenantId).HasMaxLength(64);
        builder.Property(price => price.Currency).HasMaxLength(3);
        builder.HasIndex(price => new { price.TenantId, price.ProductId }).IsUnique();
    }
}
