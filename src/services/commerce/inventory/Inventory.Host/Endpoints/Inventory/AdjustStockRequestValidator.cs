using FastEndpoints;
using FluentValidation;

namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Validates <see cref="AdjustStockRequest"/> instances.</summary>
public sealed class AdjustStockRequestValidator : Validator<AdjustStockRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AdjustStockRequestValidator"/> class.</summary>
    public AdjustStockRequestValidator()
    {
        RuleFor(request => request.Id).NotEmpty();
        RuleFor(request => request.Delta).NotEqual(0);
    }
}
