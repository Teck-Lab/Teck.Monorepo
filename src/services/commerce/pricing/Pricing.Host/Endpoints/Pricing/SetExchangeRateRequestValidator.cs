using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="SetExchangeRateRequest"/>.</summary>
public sealed class SetExchangeRateRequestValidator : Validator<SetExchangeRateRequest>
{
    /// <summary>Initializes a new instance of the <see cref="SetExchangeRateRequestValidator"/> class.</summary>
    public SetExchangeRateRequestValidator()
    {
        RuleFor(request => request.FromCurrency).NotEmpty().Length(3);
        RuleFor(request => request.ToCurrency).NotEmpty().Length(3);
        RuleFor(request => request.Rate).GreaterThan(0);
    }
}
