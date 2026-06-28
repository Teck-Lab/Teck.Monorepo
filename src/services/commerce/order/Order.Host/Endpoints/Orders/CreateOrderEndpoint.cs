using Orders.Application.Orders.Features.CreateOrder.V1;
using Orders.Application.Orders.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Orders.Host.Endpoints.Orders;

/// <summary>
/// Endpoint that creates a new order.
/// </summary>
/// <param name="bus">The message bus used to dispatch the create order command.</param>
public sealed class CreateOrderEndpoint(IMessageBus bus) : AuthenticatedEndpoint<CreateOrderRequest, OrderDto>
{
    private readonly IMessageBus _bus = bus;

    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("order", "create", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreateOrderRequest request, CancellationToken ct)
    {
        var command = new CreateOrderCommand(request.CustomerId, request.Lines);
        var result = await _bus.InvokeAsync<OrderDto>(command, ct);

        HttpContext.Response.Headers.Location = $"/orders/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/orders");
        Version(0);
    }
}
