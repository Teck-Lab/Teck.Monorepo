using Baskets.Domain.DomainEvents;
using Baskets.Domain.Services;
using Baskets.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Baskets.Domain.Entities;

/// <summary>
/// Represents a shopping basket aggregate root. A basket is owned either by an authenticated
/// authenticated subject (<see cref="Subject"/>) or, for guests, by an opaque <see cref="AnonymousToken"/>.
/// </summary>
public sealed class Basket : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<BasketItem> _items = [];

    private Basket()
    {
    }

    /// <summary>Gets the immutable authenticated owner subject, or null for a guest basket.</summary>
    public string? Subject { get; private set; }

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

    /// <summary>Gets the shopper-authorized checkout ceiling.</summary>
    public decimal AuthorizedAmount { get; private set; }

    /// <summary>Gets the checkout currency.</summary>
    public string? Currency { get; private set; }

    /// <summary>Gets the bounded opaque payment reference.</summary>
    public string? PaymentReference { get; private set; }

    /// <summary>Gets the stable checkout pricing request key.</summary>
    public string? CheckoutRequestId { get; private set; }

    /// <summary>Gets the shopper-safe pricing failure category, when checkout failed.</summary>
    public string? CheckoutFailure { get; private set; }

    /// <summary>Creates a new active basket owned by a customer.</summary>
    /// <param name="subject">The owning authenticated subject.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <returns>The new basket.</returns>
    public static Basket CreateForSubject(string subject, string tenantId)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        return new Basket { Subject = subject, TenantId = tenantId, Status = BasketStatus.Active };
    }

    /// <summary>Creates a subject-owned basket from a legacy GUID test identity.</summary>
    /// <param name="customerId">The legacy test identity converted to the persisted subject string.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <returns>The new basket.</returns>
    public static Basket CreateForCustomer(Guid customerId, string tenantId) => CreateForSubject(customerId.ToString(), tenantId);

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

    /// <summary>Adds an unpriced item; only pricing events may later set its price.</summary>
    /// <param name="productId">The product identifier.</param>
    /// <param name="productName">The product name captured in the basket.</param>
    /// <param name="quantity">The requested quantity.</param>
    public void AddItem(Guid productId, string productName, int quantity) => AddItem(productId, productName, 0m, quantity);

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

    /// <summary>Starts authoritative pricing after recording the shopper authorization ceiling.</summary>
    /// <param name="authorizedAmount">The shopper-authorized maximum total.</param>
    /// <param name="currency">The ISO authorization currency.</param>
    /// <param name="paymentReference">The bounded opaque payment reference.</param>
    public void BeginCheckout(decimal authorizedAmount, string currency, string paymentReference)
    {
        EnsureActive();
        if (_items.Count == 0)
        {
            throw new InvalidOperationException("Cannot check out an empty basket.");
        }

        if (authorizedAmount <= 0 || currency.Length != 3 || string.IsNullOrWhiteSpace(paymentReference) || paymentReference.Length > 256)
        {
            throw new ArgumentException("Checkout authorization is invalid.");
        }

        AuthorizedAmount = authorizedAmount;
        Currency = currency.ToUpperInvariant();
        PaymentReference = paymentReference;
        CheckoutRequestId = Guid.NewGuid().ToString("N");
        CheckoutFailure = null;
        Status = BasketStatus.PricingPending;
        AddDomainEvent(new BasketCheckedOut(Id, Subject, TenantId, AuthorizedAmount, Currency, CheckoutRequestId, DateTimeOffset.UtcNow));
    }

    /// <summary>Applies the only accepted source of final prices and completes checkout.</summary>
    /// <param name="pricedItems">The platform-priced basket lines.</param>
    /// <param name="subtotal">The platform-resolved subtotal.</param>
    /// <param name="currency">The authoritative ISO currency.</param>
    public void ApplyAuthoritativePricing(IReadOnlyList<BasketItem> pricedItems, decimal subtotal, string currency)
    {
        if (Status != BasketStatus.PricingPending)
        {
            throw new InvalidOperationException("Basket is not awaiting pricing.");
        }

        if (!string.Equals(Currency, currency, StringComparison.OrdinalIgnoreCase) || subtotal > AuthorizedAmount)
        {
            throw new InvalidOperationException("Authoritative price is outside the shopper authorization.");
        }

        if (pricedItems.Count != _items.Count || pricedItems.Any(item => item.Quantity <= 0 || item.UnitPrice < 0))
        {
            throw new InvalidOperationException("Authoritative prices do not match basket lines.");
        }

        foreach (BasketItem priced in pricedItems)
        {
            int index = _items.FindIndex(item => item.ProductId == priced.ProductId && item.Quantity == priced.Quantity);
            if (index < 0)
            {
                throw new InvalidOperationException("Authoritative prices contain an unknown basket line.");
            }

            _items[index] = _items[index] with { UnitPrice = priced.UnitPrice };
        }

        Subtotal = subtotal;
        Status = BasketStatus.CheckedOut;
    }

    /// <summary>Records a safe pricing failure without creating an order.</summary>
    /// <param name="failureCategory">The shopper-safe failure category.</param>
    public void FailCheckout(string failureCategory)
    {
        if (Status == BasketStatus.PricingPending)
        {
            CheckoutFailure = failureCategory;
            Status = BasketStatus.CheckoutFailed;
        }
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

    /// <summary>Transfers ownership of a guest basket to an authenticated subject.</summary>
    /// <param name="subject">The authenticated subject taking ownership.</param>
    public void AssignToSubject(string subject)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(subject);
        Subject = subject;
        AnonymousToken = null;
    }

    /// <summary>Transfers ownership from a legacy GUID test identity.</summary>
    /// <param name="customerId">The legacy test identity.</param>
    public void AssignToCustomer(Guid customerId) => AssignToSubject(customerId.ToString());

    private void EnsureActive()
    {
        if (Status != BasketStatus.Active)
        {
            throw new InvalidOperationException($"Basket is '{Status.Name}' and can no longer be modified.");
        }
    }

    private void Recalculate() => Subtotal = BasketPricingService.CalculateSubtotal(_items);
}
