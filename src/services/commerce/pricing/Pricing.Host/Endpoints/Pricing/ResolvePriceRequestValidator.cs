using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="ResolvePriceRequest"/>.</summary>
public sealed class ResolvePriceRequestValidator : Validator<ResolvePriceRequest>
{
    /// <summary>Initializes a new instance of the <see cref="ResolvePriceRequestValidator"/> class.</summary>
    public ResolvePriceRequestValidator()
    {
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.Quantity).GreaterThanOrEqualTo(1).When(request => request.Quantity.HasValue);
    }
}
