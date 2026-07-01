using FastEndpoints;
using FluentValidation;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Validates <see cref="RemoveItemRequest"/> instances.</summary>
public sealed class RemoveItemRequestValidator : Validator<RemoveItemRequest>
{
    /// <summary>Initializes a new instance of the <see cref="RemoveItemRequestValidator"/> class.</summary>
    public RemoveItemRequestValidator()
    {
        RuleFor(request => request.BasketId).NotEmpty();
        RuleFor(request => request.ProductId).NotEmpty();
    }
}
