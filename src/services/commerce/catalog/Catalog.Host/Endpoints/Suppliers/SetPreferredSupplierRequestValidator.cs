using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="SetPreferredSupplierRequest"/> instances.</summary>
public sealed class SetPreferredSupplierRequestValidator : Validator<SetPreferredSupplierRequest>
{
    /// <summary>Initializes a new instance of the <see cref="SetPreferredSupplierRequestValidator"/> class.</summary>
    public SetPreferredSupplierRequestValidator()
    {
        RuleFor(request => request.VariantId).NotEmpty();
        RuleFor(request => request.SupplierId).NotEmpty();
    }
}
