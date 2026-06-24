using Catalog.Application.Suppliers.Responses;
using Catalog.Domain.Entities;
using Riok.Mapperly.Abstractions;

namespace Catalog.Application.Suppliers.Mapping;

/// <summary>Compile-time mapping for suppliers, links, and price history.</summary>
// RequiredMappingStrategy.Target suppresses Mapperly RMG020 for the intentionally-dropped
// BaseEntity source fields (TenantId/CreatedAt/DomainEvents/...) while keeping RMG012 (unmapped
// target) active. Scope it here on the mapper — never via the repo-root .editorconfig.
[Mapper(RequiredMappingStrategy = RequiredMappingStrategy.Target)]
public static partial class SupplierMapper
{
    /// <summary>Maps a supplier to a DTO.</summary>
    public static partial SupplierDto ToDto(this Supplier supplier);

    /// <summary>Maps a variant↔supplier link to a DTO.</summary>
    public static partial VariantSupplierDto ToDto(this VariantSupplier link);

    /// <summary>Maps a single price-history row to a DTO.</summary>
    public static partial SupplierPriceHistoryDto ToHistoryDto(this SupplierPriceHistory history);

    /// <summary>Maps price-history rows to DTOs.</summary>
    public static partial IReadOnlyList<SupplierPriceHistoryDto> ToPriceHistory(this IEnumerable<SupplierPriceHistory> history);
}
