using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain.Entities;

namespace Notifications.Application.Database.Configurations;

/// <summary>Configures durable deterministic email sender receipts.</summary>
public sealed class StubEmailAcceptanceConfiguration : IEntityTypeConfiguration<StubEmailAcceptance>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<StubEmailAcceptance> builder)
    {
        builder.ToTable("stub_email_acceptances");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).HasMaxLength(64);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(256);
        builder.Property(x => x.Recipient).HasMaxLength(320);
        builder.Property(x => x.Subject).HasMaxLength(512);
        builder.Property(x => x.Body).HasMaxLength(4000);
        builder.Ignore(x => x.DomainEvents);
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
    }
}
