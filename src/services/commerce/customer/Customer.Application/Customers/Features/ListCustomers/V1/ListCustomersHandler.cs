using Customers.Application.Customers.Mapping;
using Customers.Application.Customers.ReadModels;
using Customers.Application.Customers.Responses;
using Customers.Domain.Entities;
using SharedKernel.Core.Database;

namespace Customers.Application.Customers.Features.ListCustomers.V1;

/// <summary>Handles <see cref="ListCustomersQuery"/>.</summary>
public static class ListCustomersHandler
{
    /// <summary>Returns all customers.</summary>
    /// <param name="query">The query.</param>
    /// <param name="repository">The repository used to load the customers.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task resolving to the list of customer DTOs.</returns>
    public static async Task<IReadOnlyList<CustomerDto>> Handle(
        ListCustomersQuery query,
        IGenericReadRepository<Customer, Guid> repository,
        CancellationToken ct)
    {
        var customers = await repository.ListAsync(new AllCustomersSpec(), ct).ConfigureAwait(false);
        return customers.Select(customer => customer.ToDto()).ToList();
    }
}
