using FastEndpoints;
using FluentValidation;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Validates <see cref="CheckoutBasketRequest"/> instances.</summary>
public sealed class CheckoutBasketRequestValidator : Validator<CheckoutBasketRequest>
{
    /// <summary>Initializes a new instance of the <see cref="CheckoutBasketRequestValidator"/> class.</summary>
    public CheckoutBasketRequestValidator()
    {
        RuleFor(request => request.BasketId).NotEmpty();
        RuleFor(request => request.AuthorizedAmount).GreaterThan(0);
        RuleFor(request => request.Currency).Length(3);
        RuleFor(request => request.PaymentReference).NotEmpty().MaximumLength(256);
    }
}
