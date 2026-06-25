using Catalog.Application.Suppliers.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.GetSupplierPriceHistory.V1;

/// <summary>Fetches the cost-price history for a variant↔supplier link.</summary>
public sealed record GetSupplierPriceHistoryQuery(Guid VariantId, Guid SupplierId)
    : IQuery<IReadOnlyList<SupplierPriceHistoryDto>>;
