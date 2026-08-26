using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using Notifications.Domain.Entities;
using Notifications.Domain.ValueObjects;

namespace Notifications.Application.Database.Configurations;

/// <summary>Configures durable notification deliveries.</summary>
public sealed class NotificationDeliveryConfiguration : IEntityTypeConfiguration<NotificationDelivery>
{
    /// <inheritdoc />
    public void Configure(EntityTypeBuilder<NotificationDelivery> builder)
    {
        builder.ToTable("notification_deliveries");
        builder.HasKey(x => x.Id);
        builder.Property(x => x.TenantId).HasMaxLength(64);
        builder.Property(x => x.KeycloakSubjectId).HasMaxLength(256);
        builder.Property(x => x.IdempotencyKey).HasMaxLength(256);
        builder.Property(x => x.SourceCorrelationId).HasMaxLength(256);
        builder.Property(x => x.ContactRequestId).HasMaxLength(256);
        builder.Property(x => x.Recipient).HasMaxLength(320);
        builder.Property(x => x.Subject).HasMaxLength(512);
        builder.Property(x => x.Body).HasMaxLength(4000);
        builder.Property(x => x.Kind).HasConversion(x => x.Value, x => NotificationKind.FromValue(x));
        builder.Property(x => x.Status).HasConversion(x => x.Value, x => DeliveryStatus.FromValue(x));
        builder.Ignore(x => x.DomainEvents);
        builder.HasIndex(x => new { x.TenantId, x.IdempotencyKey }).IsUnique();
        builder.HasIndex(x => new { x.TenantId, x.CustomerId });
        builder.HasIndex(x => new { x.TenantId, x.KeycloakSubjectId });
        builder.HasIndex(x => new { x.TenantId, x.OrderId });
        builder.HasIndex(x => new { x.TenantId, x.ContactRequestId });
    }
}
