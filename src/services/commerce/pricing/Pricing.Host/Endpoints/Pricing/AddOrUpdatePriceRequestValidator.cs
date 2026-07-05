using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="AddOrUpdatePriceRequest"/>.</summary>
public sealed class AddOrUpdatePriceRequestValidator : Validator<AddOrUpdatePriceRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AddOrUpdatePriceRequestValidator"/> class.</summary>
    public AddOrUpdatePriceRequestValidator()
    {
        RuleFor(request => request.Id).NotEmpty();
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.Amount).GreaterThanOrEqualTo(0);
    }
}
