using Microsoft.EntityFrameworkCore;

namespace Teck.TenantDatabase.Host.Database;

/// <summary>EF model and migration owner for the worker's central request queue.</summary>
/// <param name="options">The configured EF Core options.</param>
public sealed class TenantDatabaseDbContext(DbContextOptions<TenantDatabaseDbContext> options) : DbContext(options)
{
    /// <summary>Configures the central request queue model.</summary>
    /// <param name="modelBuilder">The EF Core model builder.</param>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var request = modelBuilder.Entity<TenantDatabaseRequestRow>();
        request.ToTable("tenant_database_requests");
        request.HasKey(row => row.SubscriptionId);
        request.Property(row => row.SubscriptionId).HasColumnName("subscription_id").HasColumnType("uuid");
        request.Property(row => row.TenantId).HasColumnName("tenant_id").HasColumnType("uuid").IsRequired();
        request.Property(row => row.DesiredState).HasColumnName("desired_state").HasColumnType("jsonb").IsRequired();
        request.Property(row => row.Phase).HasColumnName("phase").HasColumnType("text").IsRequired();
        request.Property(row => row.Detail).HasColumnName("detail").HasColumnType("text");
        request.Property(row => row.ReceivedAt).HasColumnName("received_at").HasColumnType("timestamp with time zone").IsRequired();
        request.Property(row => row.UpdatedAt).HasColumnName("updated_at").HasColumnType("timestamp with time zone").IsRequired();
        request.Property(row => row.LeaseUntil).HasColumnName("lease_until").HasColumnType("timestamp with time zone");
        request.Property(row => row.SourceOrderId).HasColumnName("source_order_id").HasColumnType("uuid").IsRequired();
        request.Property(row => row.Proof).HasColumnName("proof").HasColumnType("text").IsRequired();
        request.Property(row => row.PreviousSourceOrderId).HasColumnName("previous_source_order_id").HasColumnType("uuid").HasDefaultValue(Guid.Empty).IsRequired();
        request.Property(row => row.EvidenceId).HasColumnName("evidence_id").HasColumnType("uuid");
        request.Property(row => row.ReadinessPublishedAt).HasColumnName("readiness_published_at").HasColumnType("timestamp with time zone");
        request.Property(row => row.LastClusterUid).HasColumnName("last_cluster_uid").HasColumnType("uuid");
        request.Property(row => row.PurgeApprovalId).HasColumnName("purge_approval_id").HasColumnType("uuid");
        request.Property(row => row.PurgeProof).HasColumnName("purge_proof").HasColumnType("text");
        request.Property(row => row.PurgeMessage).HasColumnName("purge_message").HasColumnType("jsonb");
        request.Property(row => row.PurgeBackupUid).HasColumnName("purge_backup_uid").HasColumnType("uuid");
        request.Property(row => row.PurgeRestoreUid).HasColumnName("purge_restore_uid").HasColumnType("uuid");
        request.Property(row => row.PurgeBackupName).HasColumnName("purge_backup_name").HasColumnType("text");
        request.Property(row => row.PurgeVerifiedAt).HasColumnName("purge_verified_at").HasColumnType("timestamp with time zone");
        request.Property(row => row.PurgedAt).HasColumnName("purged_at").HasColumnType("timestamp with time zone");
        request.HasIndex(row => new { row.UpdatedAt, row.ReceivedAt }, "Pending")
            .HasDatabaseName("ix_tenant_database_requests_pending")
            .HasFilter("phase NOT IN ('Ready', 'NotManaged', 'Retained', 'Purging', 'Purged')");
        request.HasIndex(row => new { row.UpdatedAt, row.ReceivedAt }, "Unpublished")
            .HasDatabaseName("ix_tenant_database_requests_unpublished")
            .HasFilter("phase = 'Ready' AND readiness_published_at IS NULL");
        request.HasIndex(row => new { row.UpdatedAt, row.ReceivedAt }, "PendingPurge")
            .HasDatabaseName("ix_tenant_database_requests_pending_purge")
            .HasFilter("phase IN ('Retained', 'Purging') AND purge_message IS NOT NULL");
    }
}
