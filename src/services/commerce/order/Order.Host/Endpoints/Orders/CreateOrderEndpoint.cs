using Order.Application.Orders.Features.CreateOrder.V1;
using Order.Application.Orders.Responses;
using FastEndpoints;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Order.Host.Endpoints.Orders;

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

        HttpContext.Response.StatusCode = StatusCodes.Status201Created;
        HttpContext.Response.Headers.Location = $"/orders/{result.Id}";
        await SendAsync(result);
    }
}

public sealed record CreateOrderRequest(Guid CustomerId, List<CreateOrderLine> Lines);
