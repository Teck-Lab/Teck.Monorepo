using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain.Entities;

namespace Notifications.Application.Database.Configurations;

/// <summary>Configures customer contact projections.</summary>
public sealed class CustomerContactConfiguration : IEntityTypeConfiguration<CustomerContact>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<CustomerContact> builder)
    {
        builder.ToTable("customer_contacts");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).HasMaxLength(64);
        builder.Property(x => x.KeycloakSubjectId).HasMaxLength(256);
        builder.Property(x => x.Email).HasMaxLength(320);
        builder.Ignore(x => x.DomainEvents);
        builder.HasIndex(x => new { x.TenantId, x.CustomerId }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.KeycloakSubjectId });
    }
}
