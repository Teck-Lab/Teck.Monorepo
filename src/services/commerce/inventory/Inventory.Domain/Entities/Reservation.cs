using Inventories.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Inventories.Domain.Entities;

/// <summary>
/// Represents a source's claim on stock: an aggregate root tracking the lines and per-location
/// allocations reserved on behalf of a basket checkout or a placed order, keyed for idempotency
/// by its <see cref="SourceType"/> and <see cref="SourceId"/>.
/// </summary>
public sealed class Reservation : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<ReservationLine> _lines = [];

    private Reservation()
    {
    }

    /// <summary>Gets the kind of aggregate that originated this reservation.</summary>
    public ReservationSource SourceType { get; private set; } = ReservationSource.Basket;

    /// <summary>Gets the identifier of the originating source aggregate.</summary>
    public Guid SourceId { get; private set; }

    /// <summary>Gets the current lifecycle status of the reservation.</summary>
    public ReservationStatus Status { get; private set; } = ReservationStatus.Held;

    /// <summary>Gets the point in time at which a <see cref="ReservationStatus.Held"/> reservation expires, or null if it does not expire.</summary>
    public DateTimeOffset? ExpiresAt { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the product lines and their allocations covered by this reservation.</summary>
    public IReadOnlyList<ReservationLine> Lines => _lines;

    /// <summary>Creates a reservation that is already committed, e.g. because it originates from a placed order.</summary>
    /// <param name="source">The kind of aggregate that originated the reservation.</param>
    /// <param name="sourceId">The identifier of the originating source aggregate.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="lines">The product lines and their allocations covered by the reservation.</param>
    /// <returns>The new, already-committed reservation.</returns>
    public static Reservation CreateCommitted(
        ReservationSource source,
        Guid sourceId,
        string tenantId,
        IReadOnlyList<ReservationLine> lines)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lines);

        var reservation = new Reservation
        {
            SourceType = source,
            SourceId = sourceId,
            TenantId = tenantId,
            Status = ReservationStatus.Committed,
            ExpiresAt = null,
        };
        reservation._lines.AddRange(lines);

        return reservation;
    }

    /// <summary>Creates a reservation that holds stock pending commitment, e.g. because it originates from a checked-out basket.</summary>
    /// <param name="source">The kind of aggregate that originated the reservation.</param>
    /// <param name="sourceId">The identifier of the originating source aggregate.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="expiresAt">The point in time at which the hold expires unless committed first.</param>
    /// <param name="lines">The product lines and their allocations covered by the reservation.</param>
    /// <returns>The new, held reservation.</returns>
    public static Reservation CreateHeld(
        ReservationSource source,
        Guid sourceId,
        string tenantId,
        DateTimeOffset expiresAt,
        IReadOnlyList<ReservationLine> lines)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(lines);

        var reservation = new Reservation
        {
            SourceType = source,
            SourceId = sourceId,
            TenantId = tenantId,
            Status = ReservationStatus.Held,
            ExpiresAt = expiresAt,
        };
        reservation._lines.AddRange(lines);

        return reservation;
    }

    /// <summary>Releases the reservation, returning its stock to availability.</summary>
    public void Release()
    {
        if (!Status.IsActive)
        {
            throw new InvalidOperationException($"Reservation is '{Status.Name}' and cannot be released.");
        }

        Status = ReservationStatus.Released;
        ExpiresAt = null;
    }

    /// <summary>Marks a committed reservation as fulfilled once its stock has left inventory.</summary>
    public void Fulfil()
    {
        if (Status != ReservationStatus.Committed)
        {
            throw new InvalidOperationException($"Reservation is '{Status.Name}' and must be '{ReservationStatus.Committed.Name}' to be fulfilled.");
        }

        Status = ReservationStatus.Fulfilled;
    }

    /// <summary>Expires a held reservation whose hold period has elapsed without being committed.</summary>
    public void Expire()
    {
        if (Status != ReservationStatus.Held)
        {
            throw new InvalidOperationException($"Reservation is '{Status.Name}' and must be '{ReservationStatus.Held.Name}' to expire.");
        }

        Status = ReservationStatus.Expired;
        ExpiresAt = null;
    }

    /// <summary>
    /// Converts previously backordered quantity for a product into a real allocation now that
    /// replenished stock covers it: reduces the matching line's
    /// <see cref="ReservationLine.BackorderedQuantity"/> and extends (or adds) an
    /// <see cref="Allocation"/> at <paramref name="locationId"/> by <paramref name="quantity"/>.
    /// </summary>
    /// <param name="productId">The product identifier of the line being filled.</param>
    /// <param name="locationId">The location the newly-available stock was replenished at.</param>
    /// <param name="quantity">
    /// The quantity of backorder to convert into a real allocation. Must be positive and no more
    /// than the line's current <see cref="ReservationLine.BackorderedQuantity"/>.
    /// </param>
    public void FillBackorder(Guid productId, Guid locationId, int quantity)
    {
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        if (!Status.IsActive)
        {
            throw new InvalidOperationException($"Reservation is '{Status.Name}' and cannot fill a backorder.");
        }

        int index = _lines.FindIndex(line => line.ProductId == productId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Reservation has no line for product '{productId}'.");
        }

        ReservationLine line = _lines[index];
        if (quantity > line.BackorderedQuantity)
        {
            throw new InvalidOperationException("Cannot fill more than the line's outstanding backordered quantity.");
        }

        List<Allocation> allocations = line.Allocations.ToList();
        int allocationIndex = allocations.FindIndex(allocation => allocation.LocationId == locationId);
        if (allocationIndex >= 0)
        {
            Allocation existing = allocations[allocationIndex];
            allocations[allocationIndex] = existing with { Quantity = existing.Quantity + quantity };
        }
        else
        {
            allocations.Add(new Allocation(locationId, quantity));
        }

        _lines[index] = line with
        {
            BackorderedQuantity = line.BackorderedQuantity - quantity,
            Allocations = allocations,
        };
    }
}
