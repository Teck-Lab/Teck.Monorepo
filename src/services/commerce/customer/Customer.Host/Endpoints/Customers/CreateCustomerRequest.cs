namespace Customers.Host.Endpoints.Customers;

/// <summary>Request to create a customer.</summary>
/// <param name="Email">The customer's email address.</param>
/// <param name="FirstName">The customer's first name.</param>
/// <param name="LastName">The customer's last name.</param>
public sealed record CreateCustomerRequest(string Email, string FirstName, string LastName);
