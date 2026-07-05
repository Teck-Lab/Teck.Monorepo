using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="RemoveExchangeRateRequest"/>.</summary>
public sealed class RemoveExchangeRateRequestValidator : Validator<RemoveExchangeRateRequest>
{
    /// <summary>Initializes a new instance of the <see cref="RemoveExchangeRateRequestValidator"/> class.</summary>
    public RemoveExchangeRateRequestValidator()
    {
        RuleFor(request => request.FromCurrency).NotEmpty().Length(3);
        RuleFor(request => request.ToCurrency).NotEmpty().Length(3);
    }
}
