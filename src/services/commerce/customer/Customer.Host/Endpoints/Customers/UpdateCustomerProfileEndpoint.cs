using Customers.Application.Customers.Features.UpdateCustomerProfile.V1;
using Customers.Application.Customers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Customers.Host.Endpoints.Customers;

/// <summary>Updates a customer's profile.</summary>
/// <param name="bus">The message bus.</param>
public sealed class UpdateCustomerProfileEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<UpdateCustomerProfileRequest, CustomerDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("customer", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(UpdateCustomerProfileRequest request, CancellationToken ct)
    {
        var command = new UpdateCustomerProfileCommand(request.CustomerId, request.FirstName, request.LastName);
        var result = await bus.InvokeAsync<CustomerDto>(command, ct);
        await Send.OkAsync(result, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Put("/customers/{customerId}/profile");
        Version(0);
    }
}
