using SharedKernel.Core.Domain;

namespace Inventories.Domain.Entities;

/// <summary>
/// Represents a tenant's ordered preference of stock locations, consulted by
/// <see cref="Services.StockAllocator"/> to decide which location to allocate from first.
/// </summary>
public sealed class LocationPriority : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<Guid> _locationIds = [];

    private LocationPriority()
    {
    }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the location identifiers in descending allocation priority order.</summary>
    public IReadOnlyList<Guid> LocationIds => _locationIds;

    /// <summary>Creates a new location priority list for a tenant.</summary>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="ordered">The location identifiers in descending allocation priority order.</param>
    /// <returns>The new location priority aggregate.</returns>
    public static LocationPriority Create(string tenantId, IReadOnlyList<Guid> ordered)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        var priority = new LocationPriority
        {
            TenantId = tenantId,
        };
        priority._locationIds.AddRange(ordered);

        return priority;
    }

    /// <summary>Replaces the ordered location identifiers with a new sequence.</summary>
    /// <param name="ordered">The new location identifiers in descending allocation priority order.</param>
    public void Set(IReadOnlyList<Guid> ordered)
    {
        ArgumentNullException.ThrowIfNull(ordered);

        _locationIds.Clear();
        _locationIds.AddRange(ordered);
    }
}
