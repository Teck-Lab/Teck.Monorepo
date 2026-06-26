using FastEndpoints;
using FluentValidation;

namespace Orders.Host.Endpoints.Orders;

/// <summary>
/// Validates <see cref="CreateOrderRequest"/> instances.
/// </summary>
public sealed class CreateOrderRequestValidator : Validator<CreateOrderRequest>
{
    /// <summary>
    /// Initializes a new instance of the <see cref="CreateOrderRequestValidator"/> class.
    /// </summary>
    public CreateOrderRequestValidator()
    {
        RuleFor(request => request.CustomerId).NotEmpty();
        RuleFor(request => request.Lines).NotEmpty();
    }
}
