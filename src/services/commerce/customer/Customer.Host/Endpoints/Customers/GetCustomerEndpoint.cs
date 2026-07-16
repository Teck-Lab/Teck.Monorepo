using Customers.Application.Customers.Features.GetCustomer.V1;
using Customers.Application.Customers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Customers.Host.Endpoints.Customers;

/// <summary>Fetches a customer by identifier.</summary>
/// <param name="bus">The message bus.</param>
public sealed class GetCustomerEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<GetCustomerRequest, CustomerDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("customer", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(GetCustomerRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<CustomerDto>(new GetCustomerQuery(request.CustomerId), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/customers/{customerId}");
        Version(0);
    }
}
