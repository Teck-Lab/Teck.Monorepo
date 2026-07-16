using Customers.Application.Customers.Features.AddCustomerAddress.V1;
using Customers.Application.Customers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Customers.Host.Endpoints.Customers;

/// <summary>Adds an address to a customer.</summary>
/// <param name="bus">The message bus.</param>
public sealed class AddCustomerAddressEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<AddCustomerAddressRequest, AddressDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("customer", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(AddCustomerAddressRequest request, CancellationToken ct)
    {
        var command = new AddCustomerAddressCommand(
            request.CustomerId, request.Line1, request.Line2, request.City, request.PostalCode, request.Country);
        var result = await bus.InvokeAsync<AddressDto>(command, ct);
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/customers/{customerId}/addresses");
        Version(0);
    }
}
