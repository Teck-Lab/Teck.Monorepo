namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Request to merge a guest basket into the customer basket.</summary>
/// <param name="AnonymousToken">The guest basket token.</param>
public sealed record MergeBasketRequest(Guid AnonymousToken);
