using System.Diagnostics.CodeAnalysis;
using EntityFramework.Exceptions.PostgreSQL;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.EntityFrameworkCore;
using Finbuckle.MultiTenant.EntityFrameworkCore.Extensions;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Core.Domain;
using SharedKernel.Core.Exceptions;
using SharedKernel.Core.Pricing;
using SharedKernel.Infrastructure.MultiTenant;

namespace SharedKernel.Infrastructure.Database.EFCore;

/// <summary>
/// The base database context with support for multi-tenancy.
/// </summary>
public abstract class BaseDbContext : DbContext, IMultiTenantDbContext
{
    private readonly DatabaseStrategy _tenantStrategy;

    /// <summary>
    /// Initializes a new instance of the <see cref="BaseDbContext"/> class.
    /// </summary>
    /// <param name="options">The options to be used by a <see cref="DbContext"/>.</param>
    /// <param name="tenantDetails">The tenant information (optional).</param>
    /// <param name="tenantStrategy">The tenant database strategy (optional, defaults to Shared).</param>
    /// <param name="tenantAccessor">The multi-tenant context accessor (optional, for runtime tenant resolution).</param>
    [RequiresDynamicCode("Calls DbContext configuration which may require dynamic code at runtime.")]
    protected BaseDbContext(
        DbContextOptions options,
        TenantDetails? tenantDetails = null,
        DatabaseStrategy? tenantStrategy = null,
        IMultiTenantContextAccessor<TenantDetails>? tenantAccessor = null)
        : base(options)
    {
        // Prefer explicit tenantDetails, then accessor, then TenantDbContextOptionsExtension embedded in options
        // (used when Wolverine constructs the context via Activator.CreateInstance with a single options argument).
        var tenantId = options.FindExtension<TenantDbContextOptionsExtension>()?.TenantId;
        TenantDetails = tenantDetails
            ?? tenantAccessor?.MultiTenantContext?.TenantInfo
            ?? (tenantId is not null ? new TenantDetails { Id = tenantId, Identifier = tenantId, Name = tenantId, IsActive = true } : null);
        _tenantStrategy = tenantStrategy ?? DatabaseStrategy.Shared;
    }

    /// <summary>
    /// Gets the tenant information associated with this context.
    /// </summary>
    /// <remarks>
    /// This property satisfies the <see cref="IMultiTenantDbContext"/> interface
    /// and is used by Finbuckle.MultiTenant to apply tenant-specific filters.
    /// </remarks>
    public TenantDetails? TenantDetails { get; }

    /// <summary>
    /// Gets the tenant information as <see cref="ITenantInfo"/> for multi-tenant support.
    /// </summary>
    public ITenantInfo? TenantInfo => TenantDetails;

    /// <summary>
    /// Gets the tenant id from the TenantDetails if available.
    /// </summary>
    public string? TenantId => TenantDetails?.Id;

    /// <summary>
    /// Gets or sets the behavior when entity tenant doesn't match current tenant.
    /// </summary>
    public TenantMismatchMode TenantMismatchMode { get; set; } = TenantMismatchMode.Throw;

    /// <summary>
    /// Gets or sets the behavior when TenantInfo is null.
    /// </summary>
    public TenantNotSetMode TenantNotSetMode { get; set; } = TenantNotSetMode.Throw;

    /// <inheritdoc/>
    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        EnforceTenantOnSave();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
    {
        EnforceTenantOnSave();
        return base.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc/>
    public override Task<int> SaveChangesAsync(bool acceptAllChangesOnSuccess, CancellationToken cancellationToken = default)
    {
        EnforceTenantOnSave();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Configures conventions for the model, including Smart Enum value conversions.
    /// </summary>
    /// <param name="configurationBuilder">The configuration builder.</param>
    protected override void ConfigureConventions(ModelConfigurationBuilder configurationBuilder)
    {
        // SmartEnum configuration is handled manually in entity configurations
        // to maintain compatibility with existing string-based database storage
    }

    /// <summary>
    /// Configures the model and applies multi-tenant configuration if applicable.
    /// </summary>
    /// <param name="modelBuilder">The model builder.</param>
    [RequiresUnreferencedCode("Calls ApplyConfigurationsFromAssembly which uses reflection and may require unreferenced code.")]
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // Apply soft delete filter to all entities implementing ISoftDeletable
        modelBuilder.AppendGlobalQueryFilter<ISoftDeletable>(entity => !entity.IsDeleted);

        if (_tenantStrategy == DatabaseStrategy.None)
        {
            throw new InvalidDatabaseStrategyException("Tenant database strategy cannot be None.");
        }

        // Apply tenant model configuration for shared database strategy.
        // This must run even when TenantDetails is null (e.g., design-time migrations),
        // otherwise TenantId discriminator columns are omitted from the model.
        if (_tenantStrategy == DatabaseStrategy.Shared)
        {
            // Configure multi-tenant models with the TenantId discriminator
            modelBuilder.ConfigureMultiTenant();
        }
    }

    /// <summary>
    /// Configures additional database options.
    /// </summary>
    /// <param name="optionsBuilder">The options builder.</param>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        // Configure exception handling based on the database provider
        optionsBuilder.UseExceptionProcessor();
    }

    private void EnforceTenantOnSave()
    {
        if (_tenantStrategy == DatabaseStrategy.Shared)
        {
            this.EnforceMultiTenant();
        }
    }
}
