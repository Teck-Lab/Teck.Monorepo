using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Orders.Host.Endpoints.Orders;

public sealed class CreateOrderEndpoint(IMessageBus bus) : AuthenticatedEndpoint<CreateOrderRequest, OrderDto>
{
    private readonly IMessageBus _bus = bus;

    protected override void ConfigureEndpoint()
    {
        AllowAnonymous();
        Post("/orders");
    }

    public override async Task HandleAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var command = new CreateOrderCommand(request.CustomerId, request.Lines);
        var result = await _bus.InvokeAsync<OrderDto>(command, ct);

        HttpContext.Response.Headers.Location = $"/orders/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }
}

public sealed record CreateOrderRequest(Guid CustomerId, List<CreateOrderLine> Lines);
