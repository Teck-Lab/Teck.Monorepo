using Billings.Domain.Entities;
using Billings.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billings.Application.Database.Configurations;

/// <summary>Configures the persistence model for payment-provider attempts.</summary>
public sealed class PaymentAttemptConfiguration : IEntityTypeConfiguration<PaymentAttempt>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PaymentAttempt> builder)
    {
        builder.ToTable("payment_attempts");
        builder.HasKey(attempt => attempt.Id);
        builder.Property(attempt => attempt.TenantId).HasMaxLength(64);
        builder.Property(attempt => attempt.RequestId).HasMaxLength(128);
        builder.Property(attempt => attempt.ProviderReference).HasMaxLength(256);
        builder.Property(attempt => attempt.ProviderCode).HasMaxLength(128);
        builder.Property(attempt => attempt.Status)
            .HasConversion(status => status.Value, value => PaymentAttemptStatus.FromValue(value));
        builder.Property(attempt => attempt.DeclineCategory)
            .HasConversion<int?>(category => category == null ? null : category.Value, value => value == null ? null : DeclineCategory.FromValue(value.Value));
        builder.OwnsOne(attempt => attempt.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount").HasPrecision(18, 2);
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3);
        });
        builder.Navigation(attempt => attempt.Amount).IsRequired();
        builder.HasOne<Payment>().WithMany(payment => payment.Attempts).HasForeignKey(attempt => attempt.PaymentId).OnDelete(DeleteBehavior.Cascade);
        builder.HasIndex(attempt => new { attempt.TenantId, attempt.RequestId }).IsUnique();
        builder.HasIndex(attempt => attempt.PaymentId);
    }
}
