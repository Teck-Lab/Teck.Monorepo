using Ardalis.Specification;
using Customers.Domain.Entities;

namespace Customers.Application.Customers.ReadModels;

/// <summary>Selects all customers, ordered by last name then first name.</summary>
public sealed class AllCustomersSpec : Specification<Customer>
{
    /// <summary>Initializes a new instance of the <see cref="AllCustomersSpec"/> class.</summary>
    public AllCustomersSpec() =>
        Query.OrderBy(customer => customer.LastName).ThenBy(customer => customer.FirstName);
}
