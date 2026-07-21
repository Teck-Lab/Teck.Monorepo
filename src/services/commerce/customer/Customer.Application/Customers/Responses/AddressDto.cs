namespace Customers.Application.Customers.Responses;

/// <summary>A customer's postal address.</summary>
public sealed record AddressDto(Guid Id, string Line1, string? Line2, string City, string PostalCode, string Country, bool IsPrimary);
