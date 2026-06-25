using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Orders.Domain.Entities;

namespace Orders.Application.Database.Configurations;

public sealed class OrderConfiguration : IEntityTypeConfiguration<Order>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Order> builder)
    {
        builder.ToTable("Orders");
        builder.HasKey(o => o.Id);
        builder.Property(o => o.TenantId).HasMaxLength(64);
        builder.Ignore(o => o.DomainEvents);

        builder.OwnsMany(o => o.Lines, lines =>
        {
            lines.ToTable("OrderItems");
        });
    }
}
