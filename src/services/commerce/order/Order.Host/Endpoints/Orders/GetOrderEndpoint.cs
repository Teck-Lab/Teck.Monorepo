using Orders.Application.Orders.Features.GetOrder.V1;
using Orders.Application.Orders.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Orders.Host.Endpoints.Orders;

/// <summary>
/// Endpoint that retrieves a single order by its identifier.
/// </summary>
/// <param name="bus">The message bus used to dispatch the get order query.</param>
public sealed class GetOrderEndpoint(IMessageBus bus) : AuthenticatedEndpoint<GetOrderRequest, OrderDto>
{
    private readonly IMessageBus _bus = bus;

    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("order", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetOrderRequest request, CancellationToken ct)
    {
        var query = new GetOrderQuery(request.Id);
        var result = await _bus.InvokeAsync<OrderDto>(query, ct);

        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/orders/{id}");
        Version(0);
    }
}
