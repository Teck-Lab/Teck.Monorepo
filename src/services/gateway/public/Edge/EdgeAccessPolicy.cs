namespace Gateway.Public.Edge;

/// <summary>The resolved edge access policy for a YARP route.</summary>
/// <param name="Mode">The access mode.</param>
/// <param name="ExchangeAudience">The Keycloak audience to exchange the user token for (required unless <see cref="EdgeAccessMode.Anonymous"/>).</param>
public sealed record EdgeAccessPolicy(EdgeAccessMode Mode, string? ExchangeAudience);
