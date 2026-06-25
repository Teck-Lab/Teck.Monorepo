using Orders.Application.Orders.Features.GetOrder.V1;
using Orders.Application.Orders.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Orders.Host.Endpoints.Orders;

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

        await Send.OkAsync(result, ct);
    }
}

public sealed record GetOrderRequest(Guid Id);
