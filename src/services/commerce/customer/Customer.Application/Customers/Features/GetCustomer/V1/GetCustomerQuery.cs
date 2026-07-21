using Customers.Application.Customers.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Customers.Application.Customers.Features.GetCustomer.V1;

/// <summary>Fetches a customer by id.</summary>
public sealed record GetCustomerQuery(Guid CustomerId) : IQuery<ErrorOr<CustomerDto>>;
