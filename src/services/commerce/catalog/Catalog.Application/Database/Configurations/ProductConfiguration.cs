using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Application.Database.Configurations;

/// <summary>Maps the <see cref="Product"/> aggregate and its owned variant/supplier/history tree.</summary>
public sealed class ProductConfiguration : IEntityTypeConfiguration<Product>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Product> builder)
    {
        builder.ToTable("Products");
        builder.HasKey(p => p.Id);
        builder.Property(p => p.TenantId).HasMaxLength(64);
        builder.Property(p => p.Name).HasMaxLength(256);
        builder.Property(p => p.Description).HasMaxLength(2048);
        builder.Ignore(p => p.DomainEvents);

        builder.OwnsMany(p => p.Variants, variant =>
        {
            variant.ToTable("Variants");
            variant.WithOwner().HasForeignKey("ProductId");
            variant.HasKey(v => v.Id);
            variant.Property(v => v.Sku).HasMaxLength(128);

            variant.OwnsOne(v => v.SellPrice, money =>
            {
                money.Property(m => m.Amount).HasColumnName("SellPriceAmount").HasPrecision(18, 2);
                money.Property(m => m.Currency).HasColumnName("SellPriceCurrency").HasMaxLength(3);
            });
            variant.Navigation(v => v.SellPrice).IsRequired();

            variant.OwnsMany(v => v.Attributes, attr =>
            {
                attr.ToTable("VariantAttributes");
                attr.WithOwner().HasForeignKey("VariantId");
                // VariantAttribute has no identity of its own; key it by owner + name
                // (a variant cannot carry two attributes with the same name).
                attr.HasKey("VariantId", nameof(Domain.ValueObjects.VariantAttribute.Name));
                attr.Property(a => a.Name).HasMaxLength(128);
                attr.Property(a => a.Value).HasMaxLength(512);
            });
            variant.Navigation(v => v.Attributes).UsePropertyAccessMode(PropertyAccessMode.Field);

            variant.OwnsMany(v => v.Suppliers, link =>
            {
                link.ToTable("VariantSuppliers");
                link.WithOwner().HasForeignKey("VariantId");
                link.HasKey(l => l.Id);
                link.Property(l => l.SupplierSku).HasMaxLength(128);

                link.OwnsOne(l => l.CostPrice, money =>
                {
                    money.Property(m => m.Amount).HasColumnName("CostPriceAmount").HasPrecision(18, 2);
                    money.Property(m => m.Currency).HasColumnName("CostPriceCurrency").HasMaxLength(3);
                });
                link.Navigation(l => l.CostPrice).IsRequired();

                link.OwnsMany(l => l.PriceHistory, hist =>
                {
                    hist.ToTable("SupplierPriceHistory");
                    hist.WithOwner().HasForeignKey("VariantSupplierId");
                    hist.HasKey(h => h.Id);

                    hist.OwnsOne(h => h.CostPrice, money =>
                    {
                        money.Property(m => m.Amount).HasColumnName("CostPriceAmount").HasPrecision(18, 2);
                        money.Property(m => m.Currency).HasColumnName("CostPriceCurrency").HasMaxLength(3);
                    });
                    hist.Navigation(h => h.CostPrice).IsRequired();
                });
                link.Navigation(l => l.PriceHistory).UsePropertyAccessMode(PropertyAccessMode.Field);
            });
            variant.Navigation(v => v.Suppliers).UsePropertyAccessMode(PropertyAccessMode.Field);
        });
        builder.Navigation(p => p.Variants).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
