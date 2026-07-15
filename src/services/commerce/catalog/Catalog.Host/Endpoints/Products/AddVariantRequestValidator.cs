using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="AddVariantRequest"/> instances.</summary>
public sealed class AddVariantRequestValidator : Validator<AddVariantRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AddVariantRequestValidator"/> class.</summary>
    public AddVariantRequestValidator()
    {
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.Sku).NotEmpty();
        RuleFor(request => request.SellPriceAmount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.SellPriceCurrency).NotEmpty().Length(3);
    }
}
