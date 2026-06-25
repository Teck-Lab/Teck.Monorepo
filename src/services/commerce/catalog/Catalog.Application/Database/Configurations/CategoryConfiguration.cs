using Catalog.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Catalog.Application.Database.Configurations;

/// <summary>Maps the <see cref="Category"/> aggregate root (self-referencing hierarchy).</summary>
public sealed class CategoryConfiguration : IEntityTypeConfiguration<Category>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Category> builder)
    {
        builder.ToTable("Categories");
        builder.HasKey(c => c.Id);
        builder.Property(c => c.TenantId).HasMaxLength(64);
        builder.Property(c => c.Name).HasMaxLength(256);
        builder.Property(c => c.Slug).HasMaxLength(256);
        builder.HasIndex(c => new { c.TenantId, c.Slug });
        builder.Ignore(c => c.DomainEvents);
    }
}
