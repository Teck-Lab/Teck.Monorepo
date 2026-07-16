using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="GetSupplierPriceHistoryRequest"/> instances.</summary>
public sealed class GetSupplierPriceHistoryRequestValidator : Validator<GetSupplierPriceHistoryRequest>
{
    /// <summary>Initializes a new instance of the <see cref="GetSupplierPriceHistoryRequestValidator"/> class.</summary>
    public GetSupplierPriceHistoryRequestValidator()
    {
        RuleFor(request => request.VariantId).NotEmpty();
        RuleFor(request => request.SupplierId).NotEmpty();
    }
}
