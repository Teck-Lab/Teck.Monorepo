using FastEndpoints;
using FluentValidation;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Validates <see cref="UpdateItemRequest"/> instances.</summary>
public sealed class UpdateItemRequestValidator : Validator<UpdateItemRequest>
{
    /// <summary>Initializes a new instance of the <see cref="UpdateItemRequestValidator"/> class.</summary>
    public UpdateItemRequestValidator()
    {
        RuleFor(request => request.BasketId).NotEmpty();
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.Quantity).GreaterThan(0);
    }
}
