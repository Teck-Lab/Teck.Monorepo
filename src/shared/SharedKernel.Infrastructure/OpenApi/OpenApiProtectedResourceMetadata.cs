namespace SharedKernel.Infrastructure.OpenApi;

/// <summary>
/// Marks the Keycloak protected resource and scope required by an endpoint.
/// </summary>
/// <param name="Resource">The protected-resource name.</param>
/// <param name="Scope">The required scope on that resource.</param>
public sealed record OpenApiProtectedResourceMetadata(string Resource, string Scope);
