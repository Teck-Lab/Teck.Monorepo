using FastEndpoints;
using FluentValidation;

namespace Inventories.Host.Endpoints.Inventory;

/// <summary>Validates <see cref="RegisterStockItemRequest"/> instances.</summary>
public sealed class RegisterStockItemRequestValidator : Validator<RegisterStockItemRequest>
{
    /// <summary>Initializes a new instance of the <see cref="RegisterStockItemRequestValidator"/> class.</summary>
    public RegisterStockItemRequestValidator()
    {
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.LocationId).NotEmpty();
        RuleFor(request => request.QuantityOnHand).GreaterThanOrEqualTo(0);
        RuleFor(request => request.ReorderThreshold).GreaterThanOrEqualTo(0);
    }
}
