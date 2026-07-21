using Customers.Application.Customers.Features.ListCustomers.V1;
using Customers.Application.Customers.Responses;
using FastEndpoints;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Customers.Host.Endpoints.Customers;

/// <summary>Lists customers.</summary>
/// <param name="bus">The message bus.</param>
public sealed class ListCustomersEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<EmptyRequest, IReadOnlyList<CustomerDto>>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("customer", "read", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var result = await bus.InvokeAsync<IReadOnlyList<CustomerDto>>(new ListCustomersQuery(), ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/customers");
        Version(0);
    }
}
