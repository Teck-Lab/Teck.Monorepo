using FastEndpoints;
using FluentValidation;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Validates <see cref="AddItemRequest"/> instances.</summary>
public sealed class AddItemRequestValidator : Validator<AddItemRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AddItemRequestValidator"/> class.</summary>
    public AddItemRequestValidator()
    {
        RuleFor(request => request.BasketId).NotEmpty();
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.ProductName).NotEmpty();
        RuleFor(request => request.UnitPrice).GreaterThanOrEqualTo(0);
        RuleFor(request => request.Quantity).GreaterThan(0);
    }
}
