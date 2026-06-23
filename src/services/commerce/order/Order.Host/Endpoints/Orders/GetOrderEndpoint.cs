using Order.Application.Orders.Features.GetOrder.V1;
using Order.Application.Orders.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Order.Host.Endpoints.Orders;

public sealed class GetOrderEndpoint(IMessageBus bus) : AuthenticatedEndpoint<GetOrderRequest, OrderDto>
{
    private readonly IMessageBus _bus = bus;

    protected override void ConfigureEndpoint()
    {
        AllowAnonymous();
        Get("/orders/{id}");
    }

    public override async Task HandleAsync(GetOrderRequest request, CancellationToken ct)
    {
        var query = new GetOrderQuery(request.Id);
        var result = await _bus.InvokeAsync<OrderDto>(query, ct);

        await SendAsync(result);
    }
}

public sealed record GetOrderRequest(Guid Id);
