namespace Customers.Host.Endpoints.Customers;

/// <summary>Request to fetch a customer by identifier.</summary>
/// <param name="CustomerId">The customer identifier.</param>
public sealed record GetCustomerRequest(Guid CustomerId);
