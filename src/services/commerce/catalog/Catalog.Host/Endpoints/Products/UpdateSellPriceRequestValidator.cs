using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="UpdateSellPriceRequest"/> instances.</summary>
public sealed class UpdateSellPriceRequestValidator : Validator<UpdateSellPriceRequest>
{
    /// <summary>Initializes a new instance of the <see cref="UpdateSellPriceRequestValidator"/> class.</summary>
    public UpdateSellPriceRequestValidator()
    {
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.VariantId).NotEmpty();
        RuleFor(request => request.Amount).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Currency).NotEmpty().Length(3);
    }
}
