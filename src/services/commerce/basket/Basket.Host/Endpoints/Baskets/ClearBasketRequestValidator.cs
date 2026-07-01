using FastEndpoints;
using FluentValidation;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Validates <see cref="ClearBasketRequest"/> instances.</summary>
public sealed class ClearBasketRequestValidator : Validator<ClearBasketRequest>
{
    /// <summary>Initializes a new instance of the <see cref="ClearBasketRequestValidator"/> class.</summary>
    public ClearBasketRequestValidator()
    {
        RuleFor(request => request.BasketId).NotEmpty();
    }
}
