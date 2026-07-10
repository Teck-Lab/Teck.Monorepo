using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="ActivatePriceListRequest"/>.</summary>
public sealed class ActivatePriceListRequestValidator : Validator<ActivatePriceListRequest>
{
    /// <summary>Initializes a new instance of the <see cref="ActivatePriceListRequestValidator"/> class.</summary>
    public ActivatePriceListRequestValidator() =>
        RuleFor(request => request.Id).NotEmpty();
}
