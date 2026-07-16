using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="CreateProductRequest"/> instances.</summary>
public sealed class CreateProductRequestValidator : Validator<CreateProductRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CreateProductRequestValidator"/> class.</summary>
    public CreateProductRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty();
        RuleFor(request => request.Sku).NotEmpty();
        RuleFor(request => request.SellPriceAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.SellPriceCurrency).NotEmpty().Length(3);
    }
}
