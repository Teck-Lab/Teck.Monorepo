using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Application.Database.Configurations;

/// <summary>Maps the <see cref="Supplier"/> aggregate root.</summary>
public sealed class SupplierConfiguration : IEntityTypeConfiguration<Supplier>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Supplier> builder)
    {
        builder.ToTable("Suppliers");
        builder.HasKey(s => s.Id);
        builder.Property(s => s.TenantId).HasMaxLength(64);
        builder.Property(s => s.Name).HasMaxLength(256);
        builder.Property(s => s.ContactEmail).HasMaxLength(320);
        builder.Property(s => s.ContactPhone).HasMaxLength(64);
        builder.Ignore(s => s.DomainEvents);
    }
}
