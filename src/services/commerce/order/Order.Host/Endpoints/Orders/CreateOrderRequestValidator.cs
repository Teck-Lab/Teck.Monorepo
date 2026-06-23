using FastEndpoints;

namespace Order.Host.Endpoints.Orders;

public sealed class CreateOrderRequestValidator : Validator<CreateOrderRequest>
{
    public CreateOrderRequestValidator()
    {
        RuleFor(request => request.CustomerId).NotEmpty();
        RuleFor(request => request.Lines).NotEmpty();
    }
}
