using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="CreatePriceListRequest"/>.</summary>
public sealed class CreatePriceListRequestValidator : Validator<CreatePriceListRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CreatePriceListRequestValidator"/> class.</summary>
    public CreatePriceListRequestValidator()
    {
        RuleFor(request => request.Name).NotEmpty().MaximumLength(256);
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.Country).Length(2).When(request => request.Country is not null);
    }
}
