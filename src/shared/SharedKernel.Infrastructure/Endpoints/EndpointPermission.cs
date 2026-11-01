namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Describes the access policy of an endpoint: the Keycloak protected resource and scope it
/// requires, plus the OpenAPI audience document it belongs to.
/// </summary>
/// <param name="Resource">The Keycloak protected-resource name (empty for anonymous endpoints).</param>
/// <param name="Scope">The Keycloak scope required on the resource (empty for anonymous endpoints).</param>
/// <param name="Audience">The OpenAPI audience document group (e.g. "public", "admin").</param>
/// <param name="PolicyName">A registered JWT authorization policy when no Keycloak resource policy is used.</param>
public sealed record EndpointPermission(string Resource, string Scope, string Audience, string? PolicyName = null)
{
    /// <summary>Gets a value indicating whether this endpoint requires no authorization.</summary>
    public bool IsAnonymous => Resource.Length == 0 && PolicyName is null;

    /// <summary>Creates a permission for an endpoint that requires no authorization.</summary>
    /// <param name="audience">The OpenAPI audience document group.</param>
    /// <returns>An anonymous <see cref="EndpointPermission"/>.</returns>
    public static EndpointPermission Anonymous(string audience) => new(string.Empty, string.Empty, audience);

    /// <summary>Requires a signed JWT and a named application policy without a Keycloak resource policy.</summary>
    /// <param name="policyName">The registered server-side authorization policy.</param>
    /// <param name="audience">OpenAPI document group.</param>
    /// <returns>A non-anonymous permission governed by the registered policy.</returns>
    public static EndpointPermission ForPolicy(string policyName, string audience) =>
        new(string.Empty, string.Empty, audience, !string.IsNullOrWhiteSpace(policyName)
            ? policyName : throw new ArgumentException("A named authorization policy is required.", nameof(policyName)));
}
