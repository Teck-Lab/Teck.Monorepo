using FastEndpoints;
using FluentValidation;

namespace Baskets.Host.Endpoints.Baskets;

/// <summary>Validates <see cref="MergeBasketRequest"/> instances.</summary>
public sealed class MergeBasketRequestValidator : Validator<MergeBasketRequest>
{
    /// <summary>Initializes a new instance of the <see cref="MergeBasketRequestValidator"/> class.</summary>
    public MergeBasketRequestValidator()
    {
        RuleFor(request => request.AnonymousToken).NotEmpty();
    }
}
