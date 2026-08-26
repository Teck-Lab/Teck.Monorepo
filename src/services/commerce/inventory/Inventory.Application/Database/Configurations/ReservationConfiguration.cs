using System.Text.Json;
using Inventories.Domain.Entities;
using Inventories.Domain.ValueObjects;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventories.Application.Database.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="Reservation"/> aggregate and its owned
/// lines/allocations tree.
/// </summary>
public sealed class ReservationConfiguration : IEntityTypeConfiguration<Reservation>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Reservation> builder)
    {
        builder.ToTable("Reservations");
        builder.HasKey(reservation => reservation.Id);
        builder.Property(reservation => reservation.TenantId).HasMaxLength(64);
        builder.Ignore(reservation => reservation.DomainEvents);

        // ReservationSource and ReservationStatus are Ardalis SmartEnums — persist their
        // integer value and rebuild from it, mirroring BasketConfiguration's BasketStatus mapping.
        builder.Property(reservation => reservation.SourceType)
            .HasConversion(source => source.Value, value => ReservationSource.FromValue(value));

        builder.Property(reservation => reservation.Status)
            .HasConversion(status => status.Value, value => ReservationStatus.FromValue(value));

        builder.Property(reservation => reservation.ExpiresAt);
        builder.Property(reservation => reservation.BackorderExpiresAt);
        builder.Property(reservation => reservation.BasketId);
        builder.Property(reservation => reservation.SourceCorrelationId).HasMaxLength(128);
        builder.Property(reservation => reservation.BackorderReadyOutcomeKey).HasMaxLength(160);
        builder.Property(reservation => reservation.BackorderExpiredOutcomeKey).HasMaxLength(160);
        builder.Property(reservation => reservation.RowVersion)
            .HasColumnName("xmin")
            .HasColumnType("xid")
            .ValueGeneratedOnAddOrUpdate()
            .IsConcurrencyToken();

        // Idempotency: at most one reservation per (tenant, source). This UNIQUE index is the
        // DB-level guard so a concurrent re-delivery of the same OrderPlaced/BasketCheckedOut
        // cannot create a duplicate reservation even if both pass the application spec-check.
        builder.HasIndex(reservation => new { reservation.TenantId, reservation.SourceType, reservation.SourceId }).IsUnique();

        // Expiry sweeps key off status + expiry.
        builder.HasIndex(reservation => new { reservation.Status, reservation.ExpiresAt });
        builder.HasIndex(reservation => new { reservation.Status, reservation.BackorderExpiresAt });
        builder.HasIndex(reservation => new { reservation.TenantId, reservation.SourceCorrelationId });

        // Lines is IReadOnlyList<ReservationLine> backed by _lines; tell EF where to find the field.
        builder.Navigation(reservation => reservation.Lines).HasField("_lines");

        builder.OwnsMany(reservation => reservation.Lines, lines =>
        {
            lines.ToTable("ReservationLines");
            lines.WithOwner().HasForeignKey("ReservationId");

            // ReservationLine has no identity of its own; key it by owner + product
            // (a reservation cannot carry two lines for the same product).
            lines.HasKey("ReservationId", nameof(ReservationLine.ProductId));
            lines.Property<uint>("RowVersion")
                .HasColumnName("xmin")
                .HasColumnType("xid")
                .ValueGeneratedOnAddOrUpdate()
                .IsConcurrencyToken();

            // ReservationLine is an immutable record materialized by EF via constructor
            // binding. EF's owned-type rules forbid binding a constructor parameter that is a
            // navigation to another owned type (nested OwnsMany), which a second level of
            // OwnsMany for Allocations would require here. Allocations is therefore stored as a
            // single JSON-converted column on ReservationLines rather than its own table — this
            // keeps every ReservationLine constructor parameter scalar/converted (bindable) and
            // needs no changes to the Domain records. Revisit if/when a house-style jsonb-array
            // convention is adopted for owned primitive/value-object collections.
            var allocationsComparer = new ValueComparer<IReadOnlyList<Allocation>>(
                (left, right) => (left ?? new List<Allocation>()).SequenceEqual(right ?? new List<Allocation>()),
                list => list.Aggregate(0, (hash, allocation) => HashCode.Combine(hash, allocation)),
                list => list.ToList());

            lines.Property(line => line.Allocations)
                .HasConversion(
                    allocations => JsonSerializer.Serialize(allocations, (JsonSerializerOptions?)null),
                    json => JsonSerializer.Deserialize<List<Allocation>>(json, (JsonSerializerOptions?)null) ?? new List<Allocation>(),
                    allocationsComparer)
                .HasColumnName("Allocations");
        });
    }
}
