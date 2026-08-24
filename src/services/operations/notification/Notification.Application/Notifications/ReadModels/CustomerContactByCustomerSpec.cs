using Ardalis.Specification;
using Notifications.Domain.Entities;

namespace Notifications.Application.Notifications.ReadModels;

/// <summary>Selects a contact by customer identifier.</summary>
public sealed class CustomerContactByCustomerSpec : Specification<CustomerContact>
{
    /// <summary>Initializes the specification.</summary>
    /// <param name="customerId">The customer identifier to match.</param>
    public CustomerContactByCustomerSpec(Guid customerId) => Query.Where(x => x.CustomerId == customerId);
}
