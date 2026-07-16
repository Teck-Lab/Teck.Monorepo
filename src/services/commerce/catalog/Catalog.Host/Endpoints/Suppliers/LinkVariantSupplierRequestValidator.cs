using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="LinkVariantSupplierRequest"/> instances.</summary>
public sealed class LinkVariantSupplierRequestValidator : Validator<LinkVariantSupplierRequest>
{
    /// <summary>Initializes a new instance of the <see cref="LinkVariantSupplierRequestValidator"/> class.</summary>
    public LinkVariantSupplierRequestValidator()
    {
        RuleFor(request => request.VariantId).NotEmpty();
        RuleFor(request => request.SupplierId).NotEmpty();
        RuleFor(request => request.CostAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.CostCurrency).NotEmpty().Length(3);
        RuleFor(request => request.LeadTimeDays).GreaterThanOrEqualTo(0);
        RuleFor(request => request.MinOrderQuantity).GreaterThan(0);
    }
}
