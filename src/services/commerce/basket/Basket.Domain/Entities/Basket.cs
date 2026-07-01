using Baskets.Domain.DomainEvents;
using Baskets.Domain.Services;
using Baskets.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Baskets.Domain.Entities;

/// <summary>
/// Represents a shopping basket aggregate root. A basket is owned either by an authenticated
/// customer (<see cref="CustomerId"/>) or, for guests, by an opaque <see cref="AnonymousToken"/>.
/// </summary>
public sealed class Basket : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<BasketItem> _items = [];

    private Basket()
    {
    }

    /// <summary>Gets the identifier of the owning customer, or null for a guest basket.</summary>
    public Guid? CustomerId { get; private set; }

    /// <summary>Gets the opaque token identifying a guest basket, or null once owned by a customer.</summary>
    public Guid? AnonymousToken { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the current lifecycle status of the basket.</summary>
    public BasketStatus Status { get; private set; } = BasketStatus.Active;

    /// <summary>Gets the items currently in the basket.</summary>
    public IReadOnlyList<BasketItem> Items => _items;

    /// <summary>Gets the basket subtotal (sum of line totals).</summary>
    public decimal Subtotal { get; private set; }

    /// <summary>Creates a new active basket owned by a customer.</summary>
    /// <param name="customerId">The owning customer identifier.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <returns>The new basket.</returns>
    public static Basket CreateForCustomer(Guid customerId, string tenantId) => new()
    {
        CustomerId = customerId,
        TenantId = tenantId,
        Status = BasketStatus.Active,
    };

    /// <summary>Creates a new active guest basket identified by an anonymous token.</summary>
    /// <param name="anonymousToken">The opaque guest token.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <returns>The new basket.</returns>
    public static Basket CreateAnonymous(Guid anonymousToken, string tenantId) => new()
    {
        AnonymousToken = anonymousToken,
        TenantId = tenantId,
        Status = BasketStatus.Active,
    };

    /// <summary>Adds an item, merging by product identifier and summing quantities.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="productName">The product name captured at add-time.</param>
    /// <param name="unitPrice">The unit price captured at add-time.</param>
    /// <param name="quantity">The quantity to add (must be positive).</param>
    public void AddItem(Guid productId, string productName, decimal unitPrice, int quantity)
    {
        EnsureActive();
        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        int index = _items.FindIndex(item => item.ProductId == productId);
        if (index >= 0)
        {
            BasketItem existing = _items[index];
            _items[index] = existing with { Quantity = existing.Quantity + quantity };
        }
        else
        {
            _items.Add(new BasketItem(productId, productName, unitPrice, quantity));
        }

        Recalculate();
    }

    /// <summary>Sets the quantity for a product; a non-positive quantity removes the line.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="quantity">The new quantity; zero or less removes the line.</param>
    public void UpdateItemQuantity(Guid productId, int quantity)
    {
        EnsureActive();
        int index = _items.FindIndex(item => item.ProductId == productId);
        if (index < 0)
        {
            throw new InvalidOperationException($"Product '{productId}' is not in the basket.");
        }

        if (quantity <= 0)
        {
            _items.RemoveAt(index);
        }
        else
        {
            _items[index] = _items[index] with { Quantity = quantity };
        }

        Recalculate();
    }

    /// <summary>Removes the line for the specified product, if present.</summary>
    /// <param name="productId">The product identifier.</param>
    public void RemoveItem(Guid productId)
    {
        EnsureActive();
        _items.RemoveAll(item => item.ProductId == productId);
        Recalculate();
    }

    /// <summary>Removes all items from the basket.</summary>
    public void Clear()
    {
        EnsureActive();
        _items.Clear();
        Recalculate();
    }

    /// <summary>Marks the basket as checked out and raises <see cref="BasketCheckedOut"/>.</summary>
    public void Checkout()
    {
        EnsureActive();
        if (_items.Count == 0)
        {
            throw new InvalidOperationException("Cannot check out an empty basket.");
        }

        Status = BasketStatus.CheckedOut;
        AddDomainEvent(new BasketCheckedOut(
            Id,
            CustomerId,
            TenantId,
            Subtotal,
            _items.ToList(),
            DateTimeOffset.UtcNow));
    }

    /// <summary>Absorbs the items of another basket (merge by product, summing quantities) and marks it merged.</summary>
    /// <param name="source">The basket to merge into this one.</param>
    public void MergeFrom(Basket source)
    {
        ArgumentNullException.ThrowIfNull(source);
        EnsureActive();

        foreach (BasketItem item in source._items)
        {
            AddItem(item.ProductId, item.ProductName, item.UnitPrice, item.Quantity);
        }

        source.Status = BasketStatus.Merged;
    }

    /// <summary>Transfers ownership of a guest basket to a customer.</summary>
    /// <param name="customerId">The customer taking ownership.</param>
    public void AssignToCustomer(Guid customerId)
    {
        CustomerId = customerId;
        AnonymousToken = null;
    }

    private void EnsureActive()
    {
        if (Status != BasketStatus.Active)
        {
            throw new InvalidOperationException($"Basket is '{Status.Name}' and can no longer be modified.");
        }
    }

    private void Recalculate() => Subtotal = BasketPricingService.CalculateSubtotal(_items);
}
