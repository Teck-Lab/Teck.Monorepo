using FastEndpoints;
using FluentValidation;

namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Validates <see cref="SetLocationPrioritiesRequest"/> instances.</summary>
public sealed class SetLocationPrioritiesRequestValidator : Validator<SetLocationPrioritiesRequest>
{
    /// <summary>Initializes a new instance of the <see cref="SetLocationPrioritiesRequestValidator"/> class.</summary>
    public SetLocationPrioritiesRequestValidator()
    {
        RuleFor(request => request.LocationIds)
            .NotEmpty()
            .Must(ids => ids.Distinct().Count() == ids.Count)
            .WithMessage("LocationIds must not contain duplicates.");
    }
}
