using Ardalis.Specification;
using Customers.Domain.Entities;

namespace Customers.Application.Customers.ReadModels;

/// <summary>Selects a single customer by their linked Keycloak subject id.</summary>
public sealed class CustomerBySubjectSpec : Specification<Customer>
{
    /// <summary>Initializes the spec.</summary>
    /// <param name="subject">The Keycloak subject id of the customer to select.</param>
    public CustomerBySubjectSpec(string subject) => Query.Where(c => c.KeycloakSubjectId == subject);
}
