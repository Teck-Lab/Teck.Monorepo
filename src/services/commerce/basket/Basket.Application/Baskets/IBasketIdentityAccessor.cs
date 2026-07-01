namespace Baskets.Application.Baskets;

/// <summary>
/// Resolves the current basket owner identity: an authenticated customer, or a guest token.
/// Implemented in the host over the HTTP context.
/// </summary>
public interface IBasketIdentityAccessor
{
    /// <summary>Gets the authenticated customer identifier, or null for a guest.</summary>
    Guid? CustomerId { get; }

    /// <summary>Gets the guest basket token from the request, or null if absent.</summary>
    Guid? AnonymousToken { get; }

    /// <summary>Returns the existing guest token or mints a new one when absent.</summary>
    /// <returns>A guest basket token.</returns>
    Guid EnsureAnonymousToken();
}
