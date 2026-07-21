using Billings.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billings.Application.Database.Configurations;

/// <summary>Configures the EF Core mapping for the <see cref="Invoice"/> aggregate and its owned lines.</summary>
public sealed class InvoiceConfiguration : IEntityTypeConfiguration<Invoice>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Invoice> builder)
    {
        builder.ToTable("invoices");
        builder.HasKey(invoice => invoice.Id);
        builder.Property(invoice => invoice.TenantId).HasMaxLength(64);
        builder.Ignore(invoice => invoice.DomainEvents);

        builder.HasIndex(invoice => invoice.OrderId);

        builder.OwnsOne(invoice => invoice.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Navigation(invoice => invoice.Amount).IsRequired();

        builder.OwnsMany(invoice => invoice.Lines, line =>
        {
            line.ToTable("invoice_lines");
            line.WithOwner().HasForeignKey("InvoiceId");
            line.HasKey(l => l.Id);

            // Owned Guid PK is set in-domain (InvoiceLine.Create), not DB-generated. Without
            // ValueGeneratedNever(), EF treats the row as pre-existing and issues an UPDATE
            // instead of an INSERT for new owned lines, throwing DbUpdateConcurrencyException
            // once a real Postgres table backs this (the catalog nested-owned gotcha).
            line.Property(l => l.Id).ValueGeneratedNever();

            line.Property(l => l.Description).HasMaxLength(512);

            line.OwnsOne(l => l.UnitPrice, money =>
            {
                money.Property(m => m.Amount).HasColumnName("UnitPriceAmount").HasPrecision(18, 2);
                money.Property(m => m.Currency).HasColumnName("UnitPriceCurrency").HasMaxLength(3);
            });
            line.Navigation(l => l.UnitPrice).IsRequired();
        });
        builder.Navigation(invoice => invoice.Lines).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
