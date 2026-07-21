using Customers.Application.Customers.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Customers.Application.Customers.Features.UpdateCustomerProfile.V1;

/// <summary>Updates a customer's first and last name.</summary>
public sealed record UpdateCustomerProfileCommand(Guid CustomerId, string FirstName, string LastName) : ICommand<ErrorOr<CustomerDto>>;
