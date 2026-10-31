using Microsoft.EntityFrameworkCore;

namespace Licensing.Application.Database;

/// <summary>Licensing write context and migration target.</summary>
public sealed class LicensingDbContext : DbContext
{
    /// <summary>Creates a licensing context.</summary>
    /// <param name="options">The provider and context options.</param>
    public LicensingDbContext(DbContextOptions<LicensingDbContext> options) : base(options) { }

    /// <summary>Gets issued license records.</summary>
    public DbSet<LicenseRecord> Licenses => Set<LicenseRecord>();

    /// <summary>Gets permanent Billing suspension records per paid order, including out-of-order cancellations.</summary>
    public DbSet<SubscriptionSuspensionRecord> SubscriptionSuspensions => Set<SubscriptionSuspensionRecord>();

    /// <inheritdoc />
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var license = modelBuilder.Entity<LicenseRecord>();
        license.ToTable("licenses");
        license.HasKey(x => x.Id);
        license.HasIndex(x => x.SubscriptionId).IsUnique().HasFilter("NOT \"Revoked\"");
        license.Property(x => x.LicenseDocument).HasColumnType("text");
        license.Property(x => x.PlanId).HasMaxLength(128);
        license.Property(x => x.PlanVersion).HasMaxLength(128);
        license.Property(x => x.SigningKeyId).HasMaxLength(128);
        var suspension = modelBuilder.Entity<SubscriptionSuspensionRecord>();
        suspension.ToTable("subscription_suspensions");
        suspension.HasKey(x => x.SourceOrderId);
        suspension.HasIndex(x => x.SubscriptionId);
        suspension.HasIndex(x => x.TenantId);
    }
}
