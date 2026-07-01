using Baskets.Domain.ValueObjects;
using SharedKernel.Core.Events;

namespace Baskets.Domain.DomainEvents;

/// <summary>
/// Domain event raised when a basket has been checked out.
/// </summary>
public sealed class BasketCheckedOut : DomainEvent
{
    /// <summary>Initializes a new instance of the <see cref="BasketCheckedOut"/> class.</summary>
    /// <param name="basketId">The checked-out basket identifier.</param>
    /// <param name="customerId">The owning customer identifier.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="subtotal">The basket subtotal.</param>
    /// <param name="items">The items at checkout time.</param>
    /// <param name="checkedOutAt">The checkout timestamp.</param>
    public BasketCheckedOut(Guid basketId, Guid? customerId, string tenantId, decimal subtotal, IReadOnlyList<BasketItem> items, DateTimeOffset checkedOutAt)
    {
        BasketId = basketId;
        CustomerId = customerId;
        TenantId = tenantId;
        Subtotal = subtotal;
        Items = items;
        CheckedOutAt = checkedOutAt;
    }

    /// <summary>Gets the checked-out basket identifier.</summary>
    public Guid BasketId { get; }

    /// <summary>Gets the owning customer identifier.</summary>
    public Guid? CustomerId { get; }

    /// <summary>Gets the owning tenant identifier.</summary>
    public string TenantId { get; }

    /// <summary>Gets the basket subtotal.</summary>
    public decimal Subtotal { get; }

    /// <summary>Gets the items at checkout time.</summary>
    public IReadOnlyList<BasketItem> Items { get; }

    /// <summary>Gets the checkout timestamp.</summary>
    public DateTimeOffset CheckedOutAt { get; }
}
