using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="GetSupplierRequest"/> instances.</summary>
public sealed class GetSupplierRequestValidator : Validator<GetSupplierRequest>
{
    /// <summary>Initializes a new instance of the <see cref="GetSupplierRequestValidator"/> class.</summary>
    public GetSupplierRequestValidator() => RuleFor(request => request.SupplierId).NotEmpty();
}
