using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="UpdateSupplierCostRequest"/> instances.</summary>
public sealed class UpdateSupplierCostRequestValidator : Validator<UpdateSupplierCostRequest>
{
    /// <summary>Initializes a new instance of the <see cref="UpdateSupplierCostRequestValidator"/> class.</summary>
    public UpdateSupplierCostRequestValidator()
    {
        RuleFor(request => request.VariantId).NotEmpty();
        RuleFor(request => request.SupplierId).NotEmpty();
        RuleFor(request => request.CostAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.CostCurrency).NotEmpty().Length(3);
    }
}
