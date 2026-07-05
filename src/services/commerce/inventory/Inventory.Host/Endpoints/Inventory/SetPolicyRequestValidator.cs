using FastEndpoints;
using FluentValidation;

namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Validates <see cref="SetPolicyRequest"/> instances.</summary>
public sealed class SetPolicyRequestValidator : Validator<SetPolicyRequest>
{
    /// <summary>Initializes a new instance of the <see cref="SetPolicyRequestValidator"/> class.</summary>
    public SetPolicyRequestValidator()
    {
        RuleFor(request => request.Id).NotEmpty();
        RuleFor(request => request.ReorderThreshold).GreaterThanOrEqualTo(0);
    }
}
