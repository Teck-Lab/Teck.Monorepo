using Customers.Application.Customers.Responses;
using Customers.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Customers.Application.Customers.Mapping;

/// <summary>Compile-time mapping for customers and their addresses.</summary>
// RequiredMappingStrategy.Target suppresses Mapperly RMG020 for the intentionally-dropped
// BaseEntity source fields (TenantId/CreatedAt/DomainEvents/...) while keeping RMG012 (unmapped
// target) active. Scope it here on the mapper — never via the repo-root .editorconfig.
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class CustomerMapper
{
    /// <summary>Maps a customer to a DTO.</summary>
    public static partial CustomerDto ToDto(this Customer customer);

    /// <summary>Maps an address to a DTO.</summary>
    public static partial AddressDto ToDto(this Address address);
}
