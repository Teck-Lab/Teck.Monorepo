using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="CreateCategoryRequest"/> instances.</summary>
public sealed class CreateCategoryRequestValidator : Validator<CreateCategoryRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CreateCategoryRequestValidator"/> class.</summary>
    public CreateCategoryRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty();
        RuleFor(request => request.Slug).NotEmpty();
    }
}
