using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orders.Domain.Entities;
using Orders.Domain.ValueObjects;

namespace Orders.Application.Database.Configurations;

/// <summary>
/// Configures the EF Core mapping for the <see cref="Order"/> aggregate and its owned line items.
/// </summary>
public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.TenantId).HasMaxLength(64);
        builder.Ignore(o => o.DomainEvents);

        // OrderStatus is an Ardalis SmartEnum — persist its integer value and rebuild from it.
        builder.Property(o => o.Status)
            .HasConversion(status => status.Value, value => OrderStatus.FromValue(value));

        builder.OwnsMany(o => o.Lines, lines =>
        {
            lines.ToTable("OrderItems");
            lines.WithOwner().HasForeignKey("OrderId");

            // A line has no identity of its own; key it by owner + product
            // (an order cannot carry two lines for the same product).
            lines.HasKey("OrderId", nameof(OrderLine.ProductId));
            lines.Property(l => l.ProductName).HasMaxLength(512);

            // Total is derived (Quantity * UnitPrice) — recomputed in memory, never persisted.
            lines.Ignore(l => l.Total);
        });
    }
}
