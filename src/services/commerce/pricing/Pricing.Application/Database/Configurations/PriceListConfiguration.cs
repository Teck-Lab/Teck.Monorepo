using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;

namespace Pricing.Application.Database.Configurations;

/// <summary>Configures the EF Core mapping for the <see cref="PriceList"/> aggregate.</summary>
public sealed class PriceListConfiguration : IEntityTypeConfiguration<PriceList>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<PriceList> builder)
    {
        builder.ToTable("PriceLists");
        builder.HasKey(list => list.Id);
        builder.Ignore(list => list.DomainEvents);
        builder.Property(list => list.TenantId).HasMaxLength(64);
        builder.Property(list => list.Name).HasMaxLength(256);

        builder.Property(list => list.Status)
            .HasConversion(status => status.Value, value => PriceListStatus.FromValue(value));

        builder.OwnsOne(list => list.Scope, scope =>
        {
            scope.Property(s => s.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
            scope.Property(s => s.Country).HasColumnName("Country").HasMaxLength(2);
            scope.Property(s => s.CustomerGroupId).HasColumnName("CustomerGroupId");
            scope.Property(s => s.ChannelId).HasColumnName("ChannelId");
        });

        builder.Navigation(list => list.Prices).HasField("_prices");
        builder.HasMany(list => list.Prices)
            .WithOne(price => price.PriceList)
            .HasForeignKey(price => price.PriceListId)
            .OnDelete(DeleteBehavior.Cascade);

        builder.HasIndex(list => new { list.TenantId, list.Status });
    }
}
