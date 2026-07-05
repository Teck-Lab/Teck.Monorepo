using Inventories.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventories.Application.Database.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="StockItem"/> aggregate.
/// </summary>
public sealed class StockItemConfiguration : IEntityTypeConfiguration<StockItem>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<StockItem> builder)
    {
        builder.ToTable("StockItems");
        builder.HasKey(stockItem => stockItem.Id);
        builder.Property(stockItem => stockItem.TenantId).HasMaxLength(64);
        builder.Ignore(stockItem => stockItem.DomainEvents);

        // Enforces a single stock record per product/location per tenant.
        builder.HasIndex(s => new { s.TenantId, s.ProductId, s.LocationId }).IsUnique();

        // Postgres system column xmin backs optimistic concurrency without a dedicated column.
        builder.Property(s => s.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();
    }
}
