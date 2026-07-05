using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="UpdatePriceListRequest"/>.</summary>
public sealed class UpdatePriceListRequestValidator : Validator<UpdatePriceListRequest>
{
    /// <summary>Initializes a new instance of the <see cref="UpdatePriceListRequestValidator"/> class.</summary>
    public UpdatePriceListRequestValidator()
    {
        RuleFor(request => request.Id).NotEmpty();
        RuleFor(request => request.Name).NotEmpty().MaximumLength(256);
        RuleFor(request => request.Currency).NotEmpty().Length(3);
        RuleFor(request => request.Country).Length(2).When(request => request.Country is not null);
    }
}
