using System.Security.Claims;
using Baskets.Application.Baskets;
using MassTransit;

namespace Baskets.Host.Infrastructure;

/// <summary>
/// Resolves basket identity from the HTTP context: the authenticated <c>customer_id</c> claim, or
/// the <c>X-Basket-Token</c> header for guests.
/// </summary>
/// <param name="httpContextAccessor">The HTTP context accessor.</param>
public sealed class BasketIdentityAccessor(IHttpContextAccessor httpContextAccessor) : IBasketIdentityAccessor
{
    /// <summary>The request header carrying a guest basket token.</summary>
    public const string TokenHeader = "X-Basket-Token";

    private Guid? _minted;

    /// <inheritdoc/>
    public Guid? CustomerId
    {
        get
        {
            var value = httpContextAccessor.HttpContext?.User?.FindFirstValue("customer_id");
            return Guid.TryParse(value, out var id) ? id : null;
        }
    }

    /// <inheritdoc/>
    public Guid? AnonymousToken
    {
        get
        {
            var header = httpContextAccessor.HttpContext?.Request.Headers[TokenHeader].ToString();
            return Guid.TryParse(header, out var token) ? token : _minted;
        }
    }

    /// <inheritdoc/>
    public Guid EnsureAnonymousToken() => AnonymousToken ?? (_minted ??= NewId.Next().ToGuid());
}
