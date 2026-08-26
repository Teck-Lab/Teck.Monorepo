using Baskets.Domain.Entities;
using Baskets.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Baskets.Application.Database.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="Basket"/> aggregate and its owned items.
/// </summary>
public sealed class BasketConfiguration : IEntityTypeConfiguration<Basket>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Basket> builder)
    {
        builder.ToTable("Baskets");
        builder.HasKey(basket => basket.Id);
        builder.Property(basket => basket.TenantId).HasMaxLength(64);
        builder.Ignore(basket => basket.DomainEvents);

        // BasketStatus is an Ardalis SmartEnum — persist its integer value and rebuild from it.
        builder.Property(basket => basket.Status)
            .HasConversion(status => status.Value, value => BasketStatus.FromValue(value));

        // Lookups used by get-or-create and merge.
        builder.Property(basket => basket.Subject).HasMaxLength(256);
        builder.Property(basket => basket.Currency).HasMaxLength(3);
        builder.Property(basket => basket.PaymentReference).HasMaxLength(256);
        builder.Property(basket => basket.CheckoutRequestId).HasMaxLength(64);
        builder.Property(basket => basket.CheckoutFailure).HasMaxLength(64);
        builder.HasIndex(basket => new { basket.TenantId, basket.Subject, basket.Status });
        builder.HasIndex(basket => new { basket.TenantId, basket.AnonymousToken, basket.Status });

        // Items is IReadOnlyList<BasketItem> backed by _items; tell EF where to find the field.
        builder.Navigation(b => b.Items).HasField("_items");

        builder.OwnsMany(basket => basket.Items, items =>
        {
            items.ToTable("BasketItems");
            items.WithOwner().HasForeignKey("BasketId");
            items.HasKey("BasketId", nameof(BasketItem.ProductId));
            items.Property(item => item.ProductName).HasMaxLength(512);

            // LineTotal is derived (UnitPrice * Quantity) — recomputed in memory, never persisted.
            items.Ignore(item => item.LineTotal);
        });
    }
}
