namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Describes the access policy of an endpoint: the Keycloak protected resource and scope it
/// requires, plus the OpenAPI audience document it belongs to.
/// </summary>
/// <param name="Resource">The Keycloak protected-resource name (empty for anonymous endpoints).</param>
/// <param name="Scope">The Keycloak scope required on the resource (empty for anonymous endpoints).</param>
/// <param name="Audience">The OpenAPI audience document group (e.g. "public", "admin").</param>
public sealed record EndpointPermission(string Resource, string Scope, string Audience)
{
    /// <summary>Gets a value indicating whether this endpoint requires no authorization.</summary>
    public bool IsAnonymous => Resource.Length == 0;

    /// <summary>Creates a permission for an endpoint that requires no authorization.</summary>
    /// <param name="audience">The OpenAPI audience document group.</param>
    /// <returns>An anonymous <see cref="EndpointPermission"/>.</returns>
    public static EndpointPermission Anonymous(string audience) => new(string.Empty, string.Empty, audience);
}
