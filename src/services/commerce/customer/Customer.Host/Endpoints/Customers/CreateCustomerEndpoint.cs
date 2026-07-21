using Customers.Application.Customers.Features.CreateCustomer.V1;
using Customers.Application.Customers.Responses;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Customers.Host.Endpoints.Customers;

/// <summary>Creates a customer.</summary>
/// <param name="bus">The message bus.</param>
public sealed class CreateCustomerEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<CreateCustomerRequest, CustomerDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("customer", "manage", "public");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreateCustomerRequest request, CancellationToken ct)
    {
        var command = new CreateCustomerCommand(request.Email, request.FirstName, request.LastName);
        var result = await bus.InvokeAsync<CustomerDto>(command, ct);
        HttpContext.Response.Headers.Location = $"/customers/{result.Id}";
        await Send.ResponseAsync(result, StatusCodes.Status201Created, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/customers");
        Version(0);
    }
}
