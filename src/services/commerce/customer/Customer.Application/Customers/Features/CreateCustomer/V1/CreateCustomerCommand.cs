using Customers.Application.Customers.Responses;
using SharedKernel.Core.CQRS;

namespace Customers.Application.Customers.Features.CreateCustomer.V1;

/// <summary>Creates a customer linked to the caller's Keycloak identity.</summary>
public sealed record CreateCustomerCommand(string Email, string FirstName, string LastName) : ICommand<CustomerDto>;
