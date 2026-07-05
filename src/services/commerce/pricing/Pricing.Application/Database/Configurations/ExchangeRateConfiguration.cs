using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pricing.Domain.Entities;

namespace Pricing.Application.Database.Configurations;

/// <summary>Configures the EF Core mapping for the <see cref="ExchangeRate"/> aggregate.</summary>
public sealed class ExchangeRateConfiguration : IEntityTypeConfiguration<ExchangeRate>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<ExchangeRate> builder)
    {
        builder.ToTable("ExchangeRates");
        builder.HasKey(rate => rate.Id);
        builder.Ignore(rate => rate.DomainEvents);
        builder.Property(rate => rate.TenantId).HasMaxLength(64);
        builder.Property(rate => rate.FromCurrency).HasMaxLength(3).IsRequired();
        builder.Property(rate => rate.ToCurrency).HasMaxLength(3).IsRequired();

        builder.HasIndex(rate => new { rate.TenantId, rate.FromCurrency, rate.ToCurrency })
            .IsUnique()
            .HasFilter("\"IsDeleted\" = false");
    }
}
