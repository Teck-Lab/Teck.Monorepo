using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="GetPriceListRequest"/>.</summary>
public sealed class GetPriceListRequestValidator : Validator<GetPriceListRequest>
{
    /// <summary>Initializes a new instance of the <see cref="GetPriceListRequestValidator"/> class.</summary>
    public GetPriceListRequestValidator() =>
        RuleFor(request => request.Id).NotEmpty();
}
