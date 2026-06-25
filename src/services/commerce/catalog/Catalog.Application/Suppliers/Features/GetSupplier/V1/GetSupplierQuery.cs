using Catalog.Application.Suppliers.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.GetSupplier.V1;

/// <summary>Fetches a supplier by id.</summary>
public sealed record GetSupplierQuery(Guid SupplierId) : IQuery<SupplierDto>;
