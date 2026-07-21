using Customers.Application.Customers.Responses;
using SharedKernel.Core.CQRS;

namespace Customers.Application.Customers.Features.ListCustomers.V1;

/// <summary>Lists all customers.</summary>
public sealed record ListCustomersQuery : IQuery<IReadOnlyList<CustomerDto>>;
