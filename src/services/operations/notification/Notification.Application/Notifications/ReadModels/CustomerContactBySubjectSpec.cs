using Ardalis.Specification;
using Notifications.Domain.Entities;

namespace Notifications.Application.Notifications.ReadModels;

/// <summary>Selects a contact by immutable customer subject.</summary>
public sealed class CustomerContactBySubjectSpec : Specification<CustomerContact>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="subject">The immutable customer subject to match.</param>
    public CustomerContactBySubjectSpec(string subject) => Query.Where(x => x.KeycloakSubjectId == subject);
}
