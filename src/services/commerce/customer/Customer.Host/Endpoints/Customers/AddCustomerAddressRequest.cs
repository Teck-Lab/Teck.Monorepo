namespace Customers.Host.Endpoints.Customers;

/// <summary>Request to add an address to a customer.</summary>
/// <param name="CustomerId">The customer identifier.</param>
/// <param name="Line1">The first address line.</param>
/// <param name="Line2">The optional second address line.</param>
/// <param name="City">The city.</param>
/// <param name="PostalCode">The postal code.</param>
/// <param name="Country">The country.</param>
public sealed record AddCustomerAddressRequest(
    Guid CustomerId, string Line1, string? Line2, string City, string PostalCode, string Country);
