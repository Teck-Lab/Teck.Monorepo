using System.Security.Claims;
using Baskets.Application.Baskets;

namespace Baskets.Host.Infrastructure;

/// <summary>
/// Resolves basket identity from the HTTP context: the authenticated standard <c>sub</c> claim, or
/// the <c>X-Basket-Token</c> header for guests.
/// </summary>
/// <param name="httpContextAccessor">The HTTP context accessor.</param>
public sealed class BasketIdentityAccessor(IHttpContextAccessor httpContextAccessor) : IBasketIdentityAccessor
{
    /// <summary>The request header carrying a guest basket token.</summary>
    public const string TokenHeader = "X-Basket-Token";

    private Guid? _minted;

    /// <inheritdoc/>
    public string? Subject
    {
        get
        {
            var user = httpContextAccessor.HttpContext?.User;
            return user?.FindFirstValue("sub") ?? user?.FindFirstValue(ClaimTypes.NameIdentifier);
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
    public Guid EnsureAnonymousToken() => AnonymousToken ?? (_minted ??= Guid.NewGuid());
}
