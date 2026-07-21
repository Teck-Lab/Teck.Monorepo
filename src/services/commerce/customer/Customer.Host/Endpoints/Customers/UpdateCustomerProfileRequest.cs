namespace Customers.Host.Endpoints.Customers;

/// <summary>Request to update a customer's profile.</summary>
/// <param name="CustomerId">The customer identifier.</param>
/// <param name="FirstName">The customer's first name.</param>
/// <param name="LastName">The customer's last name.</param>
public sealed record UpdateCustomerProfileRequest(Guid CustomerId, string FirstName, string LastName);
