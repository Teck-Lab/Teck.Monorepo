using Ardalis.Specification;
using Customers.Domain.Entities;

namespace Customers.Application.Customers.ReadModels;

/// <summary>Selects a single customer by id.</summary>
public sealed class CustomerByIdSpec : Specification<Customer>
{
    /// <summary>Initializes the spec.</summary>
    /// <param name="customerId">The identifier of the customer to select.</param>
    public CustomerByIdSpec(Guid customerId) => Query.Where(c => c.Id == customerId);
}
