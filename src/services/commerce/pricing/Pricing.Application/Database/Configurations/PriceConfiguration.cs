using System.Globalization;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Microsoft.EntityFrameworkCore.Storage.ValueConversion;
using Pricing.Domain.Entities;
using Pricing.Domain.ValueObjects;

namespace Pricing.Application.Database.Configurations;

/// <summary>Configures the EF Core mapping for the <see cref="Price"/> entity and its owned tiers.</summary>
public sealed class PriceConfiguration : IEntityTypeConfiguration<Price>
{
    /// <summary>Serializes a tier <see cref="Money"/> to/from a single packed "amount|currency" column value.</summary>
    private static readonly ValueConverter<Money, string> MoneyConverter = new(
        money => money.Amount.ToString(CultureInfo.InvariantCulture) + "|" + money.Currency,
        raw => ParseMoney(raw));

    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Price> builder)
    {
        builder.ToTable("Prices");
        builder.HasKey(price => price.Id);
        builder.Ignore(price => price.DomainEvents);
        builder.Property(price => price.TenantId).HasMaxLength(64);

        // Base amount as an owned Money (Amount + Currency columns). Price is an ordinary class
        // with a private parameterless constructor, so EF materializes it via property/field
        // access rather than constructor binding — the nested owned Money causes no conflict here.
        builder.OwnsOne(price => price.Amount, money =>
        {
            money.Property(m => m.Amount).HasColumnName("Amount");
            money.Property(m => m.Currency).HasColumnName("Currency").HasMaxLength(3).IsRequired();
        });
        builder.Navigation(price => price.Amount).IsRequired();

        // Tiers as an owned collection, one row per tier in its own "PriceTiers" table.
        //
        // PriceTier is a positional record, so its ONLY usable constructor is
        // `PriceTier(int MinQuantity, Money Amount)` (the compiler-generated copy constructor
        // `PriceTier(PriceTier original)` cannot bind either). EF Core's constructor-binding
        // convention requires every constructor parameter to map to a genuinely scalar
        // (primitive/enum/converted) property; a parameter that resolves to a nested structured
        // type fails, and this restriction is identical whether that nested type is mapped as an
        // owned entity (`tier.OwnsOne(t => t.Amount, ...)`) or as a complex type
        // (`tier.ComplexProperty(t => t.Amount, ...)` / `EntityTypeBuilder.ComplexCollection`
        // with a nested `ComplexProperty`) — both were tried and both fail model build with:
        //   "Cannot bind 'Amount' in 'PriceTier(int MinQuantity, Money Amount)' ...
        //    Navigations to related entities, including references to owned types, cannot be
        //    bound."
        // The fix that keeps PriceTier's own constructor binding valid is to map `Amount` as a
        // single scalar column via `HasConversion` rather than as a nested owned/complex type:
        // a converted property keeps its declared CLR type (Money) in the model — so
        // constructor-parameter matching by name+type still succeeds — while the column itself
        // stores a packed "amount|currency" string. Money is still a first-class `Money` in the
        // domain and in the EF model; only the physical column shape for this one nested case
        // changes from two columns (Amount, Currency) to one packed column.
        builder.Navigation(price => price.Tiers).HasField("_tiers");
        builder.OwnsMany(price => price.Tiers, tier =>
        {
            tier.ToTable("PriceTiers");
            tier.WithOwner().HasForeignKey("PriceId");
            tier.Property(t => t.MinQuantity);
            tier.HasKey("PriceId", nameof(PriceTier.MinQuantity));
            tier.Property(t => t.Amount)
                .HasConversion(MoneyConverter)
                .HasColumnName("Amount")
                .IsRequired();
        });

        // The resolution hot-path key.
        builder.HasIndex(price => new { price.TenantId, price.ProductId });
    }

    private static Money ParseMoney(string raw)
    {
        int separator = raw.IndexOf('|', StringComparison.Ordinal);
        decimal amount = decimal.Parse(raw.AsSpan(0, separator), NumberStyles.Number, CultureInfo.InvariantCulture);
        string currency = raw[(separator + 1)..];
        return new Money(amount, currency);
    }
}
