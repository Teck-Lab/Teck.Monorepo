using Catalog.Application.Suppliers.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.UpdateSupplierCost.V1;

/// <summary>Changes a variant↔supplier cost price (recorded in history).</summary>
public sealed record UpdateSupplierCostCommand(Guid VariantId, Guid SupplierId, decimal CostAmount, string CostCurrency)
    : ICommand<ErrorOr<VariantSupplierDto>>;
