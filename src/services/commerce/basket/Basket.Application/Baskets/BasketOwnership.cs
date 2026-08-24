using Baskets.Domain.Entities;

namespace Baskets.Application.Baskets;

/// <summary>
/// Object-level authorization guard: verifies the current caller owns a basket before it is read
/// for mutation. Prevents an IDOR where a leaked <c>BasketId</c> lets a third party mutate or check
/// out another owner's basket.
/// </summary>
public static class BasketOwnership
{
    /// <summary>
    /// Throws if <paramref name="basket"/> is not owned by the caller resolved from
    /// <paramref name="identity"/> — a subject-owned basket must match the caller's subject, and a
    /// guest basket must match the caller's anonymous token.
    /// </summary>
    /// <param name="basket">The basket loaded by id.</param>
    /// <param name="identity">The current caller identity.</param>
    /// <exception cref="UnauthorizedAccessException">The basket belongs to a different customer or guest.</exception>
    public static void EnsureOwnedBy(Basket basket, IBasketIdentityAccessor identity)
    {
        bool owned = !string.IsNullOrWhiteSpace(basket.Subject)
            ? string.Equals(identity.Subject, basket.Subject, StringComparison.Ordinal)
            : basket.AnonymousToken is Guid token && identity.AnonymousToken == token;

        if (!owned)
        {
            throw new UnauthorizedAccessException($"Basket '{basket.Id}' is not owned by the current caller.");
        }
    }
}
