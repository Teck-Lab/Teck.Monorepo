using Inventories.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Inventories.Application.Database.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="LocationPriority"/> aggregate.
/// </summary>
public sealed class LocationPriorityConfiguration : IEntityTypeConfiguration<LocationPriority>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<LocationPriority> builder)
    {
        builder.ToTable("LocationPriorities");
        builder.HasKey(priority => priority.Id);
        builder.Property(priority => priority.TenantId).HasMaxLength(64);
        builder.Ignore(priority => priority.DomainEvents);

        builder.HasIndex(priority => priority.TenantId).IsUnique();

        // No repo precedent exists for mapping a list of primitives (order/basket/catalog all
        // model collections as owned entities). LocationIds is a single ordered value — not a
        // collection of independent rows worth their own table/join — so it is stored as a
        // single delimited-string column via a value conversion, with a ValueComparer so EF's
        // change tracker compares by content instead of reference. If a jsonb column becomes the
        // house style for primitive lists, revisit this in favor of that.
        var comparer = new ValueComparer<IReadOnlyList<Guid>>(
            (left, right) => (left ?? new List<Guid>()).SequenceEqual(right ?? new List<Guid>()),
            list => list.Aggregate(0, (hash, id) => HashCode.Combine(hash, id.GetHashCode())),
            list => list.ToList());

        builder.Property(priority => priority.LocationIds)
            .HasField("_locationIds")
            .HasConversion(
                ids => string.Join(',', ids),
                value => string.IsNullOrEmpty(value)
                    ? new List<Guid>()
                    : value.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(Guid.Parse).ToList(),
                comparer)
            .HasColumnName("LocationIds");
    }
}
