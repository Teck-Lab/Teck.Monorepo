using Customers.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Customers.Application.Database.Configurations;

/// <summary>Maps the <see cref="Customer"/> aggregate and its owned <see cref="Address"/> collection.</summary>
public sealed class CustomerConfiguration : IEntityTypeConfiguration<Customer>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<Customer> builder)
    {
        builder.ToTable("customers");
        builder.HasKey(customer => customer.Id);
        builder.Property(customer => customer.TenantId).HasMaxLength(64);
        builder.Property(customer => customer.KeycloakSubjectId).HasMaxLength(128);
        builder.Property(customer => customer.Email).HasMaxLength(320);
        builder.Property(customer => customer.FirstName).HasMaxLength(128);
        builder.Property(customer => customer.LastName).HasMaxLength(128);
        builder.HasIndex(customer => customer.KeycloakSubjectId).IsUnique();
        builder.Ignore(customer => customer.DomainEvents);

        builder.OwnsMany(customer => customer.Addresses, address =>
        {
            address.ToTable("addresses");
            address.WithOwner().HasForeignKey("CustomerId");
            address.HasKey(a => a.Id);
            address.Property(a => a.Id).ValueGeneratedNever();
            address.Property(a => a.Line1).HasMaxLength(256);
            address.Property(a => a.Line2).HasMaxLength(256);
            address.Property(a => a.City).HasMaxLength(128);
            address.Property(a => a.PostalCode).HasMaxLength(32);
            address.Property(a => a.Country).HasMaxLength(64);
        });
        builder.Navigation(customer => customer.Addresses).UsePropertyAccessMode(PropertyAccessMode.Field);
    }
}
