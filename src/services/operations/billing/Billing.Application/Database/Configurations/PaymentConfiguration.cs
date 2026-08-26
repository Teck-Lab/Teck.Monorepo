using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
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
        builder.Property(payment => payment.PaymentMethodToken).HasMaxLength(256);
        builder.Property(payment => payment.RequestId).HasMaxLength(128);
        builder.Property(payment => payment.SourceCorrelationId).HasMaxLength(128);
        builder.Property(payment => payment.CancellationRequestId).HasMaxLength(128);
        builder.Property(payment => payment.DeclineMappingAuditHash).HasMaxLength(64);
        builder.Property(payment => payment.DeclineCategory)
            .HasConversion<int?>(category => category == null ? null : category.Value, value => value == null ? null : DeclineCategory.FromValue(value.Value));

        builder.OwnsOne(payment => payment.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Navigation(payment => payment.Amount).IsRequired();

        builder.OwnsOne(payment => payment.AuthorizedAmount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("AuthorizedAmount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("AuthorizedCurrency").HasMaxLength(3);
        });
        builder.Navigation(payment => payment.AuthorizedAmount).IsRequired();

        // Idempotency: at most one payment per order.
        builder.HasIndex(payment => payment.OrderId).IsUnique();
        builder.HasIndex(payment => new { payment.TenantId, payment.RequestId }).IsUnique();
    }
}
