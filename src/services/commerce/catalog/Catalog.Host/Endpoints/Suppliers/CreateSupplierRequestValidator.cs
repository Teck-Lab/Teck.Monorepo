using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Suppliers;

/// <summary>Validates <see cref="CreateSupplierRequest"/> instances.</summary>
public sealed class CreateSupplierRequestValidator : Validator<CreateSupplierRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CreateSupplierRequestValidator"/> class.</summary>
    public CreateSupplierRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty();
        When(request => request.ContactEmail is not null, () =>
            RuleFor(request => request.ContactEmail!).EmailAddress());
    }
}
