using Billings.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;

namespace Billings.Application.Database.Configurations;

/// <summary>Maps durable purchase order snapshots.</summary>
public sealed class TenantPurchaseOrderConfiguration : IEntityTypeConfiguration<TenantPurchaseOrder>
{
    /// <inheritdoc/>
    public void Configure(EntityTypeBuilder<TenantPurchaseOrder> builder)
    {
        builder.ToTable("tenant_purchase_orders");
        builder.HasKey(order => order.Id);
        builder.Property(order => order.Id).ValueGeneratedNever();
        builder.Property(order => order.TenantIdentifier).HasMaxLength(128).IsRequired();
        builder.Property(order => order.PlanId).HasMaxLength(64).IsRequired();
        builder.Property(order => order.PlanVersion).HasMaxLength(64).IsRequired();
        builder.Property(order => order.Currency).HasMaxLength(3).IsRequired();
        builder.Property(order => order.DatabaseStrategy).HasMaxLength(32).IsRequired();
        builder.Property(order => order.DatabaseProvider).HasMaxLength(32).IsRequired();
        builder.Property(order => order.Status).HasMaxLength(16).IsRequired();
        builder.Property(order => order.PaymentMethod).HasMaxLength(32);
        builder.Property(order => order.ExternalInvoiceReference).HasMaxLength(128);
        builder.Property(order => order.InvoiceVerifiedBy).HasMaxLength(128);
        builder.Property(order => order.StripePaymentIntentId).HasMaxLength(255);
        builder.HasIndex(order => order.StripePaymentIntentId).IsUnique();
        builder.Property(order => order.CreatedBySubjectId).HasMaxLength(128).IsRequired();
        builder.Property(order => order.BeneficiarySubjectId).HasMaxLength(128).IsRequired();
        builder.HasIndex(order => order.PreviousPurchaseOrderId)
            .IsUnique().HasFilter("\"PreviousPurchaseOrderId\" <> '00000000-0000-0000-0000-000000000000'");
        builder.HasIndex(order => order.TenantIdentifier);
        builder.HasIndex(order => order.TenantId);
        builder.HasIndex(order => order.SubscriptionId).HasFilter("\"SubscriptionId\" <> '00000000-0000-0000-0000-000000000000'");
        builder.HasIndex(order => order.TenantId, "OnePendingPerTenant")
            .HasDatabaseName("IX_tenant_purchase_orders_one_pending_per_tenant")
            .IsUnique().HasFilter("\"Status\" = 'pending'");
        builder.HasIndex(order => order.Status);
    }
}
