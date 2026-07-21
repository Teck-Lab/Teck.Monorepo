namespace Customers.Application.Customers;

/// <summary>
/// Resolves the current authenticated customer identity from Keycloak.
/// Implemented in the host over the HTTP context.
/// </summary>
public interface ICustomerIdentityAccessor
{
    /// <summary>Gets the authenticated customer's Keycloak subject identifier, or null if unauthenticated.</summary>
    string? KeycloakSubjectId { get; }
}
