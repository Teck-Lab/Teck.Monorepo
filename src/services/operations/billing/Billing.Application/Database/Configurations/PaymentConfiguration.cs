using Billings.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billings.Application.Database.Configurations;

/// <summary>Configures the EF Core mapping for the <see cref="Payment"/> aggregate.</summary>
public sealed class PaymentConfiguration : IEntityTypeConfiguration<Payment>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Payment> builder)
    {
        builder.ToTable("payments");
        builder.HasKey(payment => payment.Id);
        builder.Property(payment => payment.TenantId).HasMaxLength(64);
        builder.Ignore(payment => payment.DomainEvents);

        // PaymentStatus is an Ardalis SmartEnum — persist its integer value and rebuild from it.
        builder.Property(payment => payment.Status)
            .HasConversion(status => status.Value, value => PaymentStatus.FromValue(value));

        builder.Property(payment => payment.ProviderReference).HasMaxLength(256);

        builder.OwnsOne(payment => payment.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Navigation(payment => payment.Amount).IsRequired();

        // Idempotency: at most one payment per order.
        builder.HasIndex(payment => payment.OrderId).IsUnique();
    }
}
