using Catalog.Application.Suppliers.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Suppliers.Features.LinkVariantSupplier.V1;

/// <summary>Links a supplier to a variant with sourcing details.</summary>
public sealed record LinkVariantSupplierCommand(
    Guid VariantId,
    Guid SupplierId,
    decimal CostAmount,
    string CostCurrency,
    string SupplierSku,
    int LeadTimeDays,
    int MinOrderQuantity,
    bool IsPreferred) : ICommand<ErrorOr<VariantSupplierDto>>;
