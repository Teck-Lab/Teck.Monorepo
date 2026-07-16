namespace Customers.Application.Customers.Responses;

/// <summary>A customer.</summary>
public sealed record CustomerDto(Guid Id, string KeycloakSubjectId, string Email, string FirstName, string LastName, bool IsActive, IReadOnlyList<AddressDto> Addresses);
