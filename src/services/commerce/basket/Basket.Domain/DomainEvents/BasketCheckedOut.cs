using SharedKernel.Core.Events;

namespace Baskets.Domain.DomainEvents;

/// <summary>
/// Domain event raised when a basket has been checked out.
/// </summary>
public sealed class BasketCheckedOut : DomainEvent
{
    /// <summary>Initializes a new instance of the <see cref="BasketCheckedOut"/> class.</summary>
    /// <param name="basketId">The checked-out basket identifier.</param>
    /// <param name="subject">The owning authenticated subject.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="authorizedAmount">The shopper authorization ceiling.</param>
    /// <param name="currency">The checkout currency.</param>
    /// <param name="requestId">The stable pricing request key.</param>
    /// <param name="checkedOutAt">The checkout timestamp.</param>
    public BasketCheckedOut(Guid basketId, string? subject, string tenantId, decimal authorizedAmount, string currency, string requestId, DateTimeOffset checkedOutAt)
    {
        BasketId = basketId;
        Subject = subject;
        TenantId = tenantId;
        AuthorizedAmount = authorizedAmount;
        Currency = currency;
        RequestId = requestId;
        CheckedOutAt = checkedOutAt;
    }

    /// <summary>Gets the checked-out basket identifier.</summary>
    public Guid BasketId { get; }

    /// <summary>Gets the owning authenticated subject.</summary>
    public string? Subject { get; }

    /// <summary>Gets the owning tenant identifier.</summary>
    public string TenantId { get; }

    /// <summary>Gets the shopper authorization ceiling.</summary>
    public decimal AuthorizedAmount { get; }

    /// <summary>Gets the checkout currency.</summary>
    public string Currency { get; }

    /// <summary>Gets the stable pricing request key.</summary>
    public string RequestId { get; }

    /// <summary>Gets the checkout timestamp.</summary>
    public DateTimeOffset CheckedOutAt { get; }
}
