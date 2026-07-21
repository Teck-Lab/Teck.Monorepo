using SharedKernel.Core.Events;

namespace Customers.Domain.DomainEvents;

/// <summary>Raised when a new customer is created.</summary>
public sealed class CustomerCreated : DomainEvent
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CustomerCreated"/> class.
    /// </summary>
    /// <param name="customerId">The id of the created customer.</param>
    /// <param name="tenantId">The owning tenant id.</param>
    /// <param name="keycloakSubjectId">The linked Keycloak subject id.</param>
    /// <param name="email">The customer's email address.</param>
    public CustomerCreated(Guid customerId, string tenantId, string keycloakSubjectId, string email)
    {
        CustomerId = customerId;
        TenantId = tenantId;
        KeycloakSubjectId = keycloakSubjectId;
        Email = email;
    }

    /// <summary>Gets the id of the created customer.</summary>
    public Guid CustomerId { get; }

    /// <summary>Gets the owning tenant id.</summary>
    public string TenantId { get; }

    /// <summary>Gets the linked Keycloak subject id.</summary>
    public string KeycloakSubjectId { get; }

    /// <summary>Gets the customer's email address.</summary>
    public string Email { get; }
}
