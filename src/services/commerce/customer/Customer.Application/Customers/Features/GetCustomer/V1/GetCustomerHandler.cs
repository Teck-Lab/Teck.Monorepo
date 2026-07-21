using Customers.Application.Customers.Mapping;
using Customers.Application.Customers.ReadModels;
using Customers.Application.Customers.Responses;
using Customers.Domain.Entities;
using ErrorOr;
using SharedKernel.Core.Database;

namespace Customers.Application.Customers.Features.GetCustomer.V1;

/// <summary>Handles <see cref="GetCustomerQuery"/>.</summary>
public static class GetCustomerHandler
{
    /// <summary>Returns the customer DTO or a NotFound error.</summary>
    /// <param name="query">The query identifying the customer to return.</param>
    /// <param name="repository">The repository used to load the customer.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task resolving to the customer DTO or a NotFound error.</returns>
    public static async Task<ErrorOr<CustomerDto>> Handle(
        GetCustomerQuery query,
        IGenericReadRepository<Customer, Guid> repository,
        CancellationToken ct)
    {
        var customer = await repository.FirstOrDefaultAsync(new CustomerByIdSpec(query.CustomerId), ct).ConfigureAwait(false);

        return customer is null
            ? Error.NotFound(description: $"Customer '{query.CustomerId}' was not found.")
            : customer.ToDto();
    }
}
