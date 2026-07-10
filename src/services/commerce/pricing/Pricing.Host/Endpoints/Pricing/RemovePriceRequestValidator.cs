using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="RemovePriceRequest"/>.</summary>
public sealed class RemovePriceRequestValidator : Validator<RemovePriceRequest>
{
    /// <summary>Initializes a new instance of the <see cref="RemovePriceRequestValidator"/> class.</summary>
    public RemovePriceRequestValidator()
    {
        RuleFor(request => request.Id).NotEmpty();
        RuleFor(request => request.ProductId).NotEmpty();
    }
}
