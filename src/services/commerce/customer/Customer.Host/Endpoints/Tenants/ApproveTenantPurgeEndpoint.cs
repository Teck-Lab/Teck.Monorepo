using System.Security.Claims;
using Customers.Application.Tenants;
using FastEndpoints;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Customers.Host.Endpoints.Tenants;

/// <summary>Records an authenticated operator's approval without accepting tenant or paid-order data from a body.</summary>
/// <param name="bus">The Customer transactional handler and outbox.</param>
public sealed class ApproveTenantPurgeEndpoint(IMessageBus bus)
    : AuthenticatedEndpoint<EmptyRequest, TenantPurgeApprovedIntegrationEvent>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => new("customer", "purge", "admin");

    /// <inheritdoc/>
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var subject = HttpContext.User.FindFirst("sub")?.Value ??
            HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        try
        {
            TenantPurgeApprovalResult result = await bus.InvokeAsync<TenantPurgeApprovalResult>(
                new ApproveTenantPurgeCommand(Route<Guid>("tenantId"), subject), ct).ConfigureAwait(false);
            if (!result.Found)
            {
                await Send.NotFoundAsync(ct);
            }
            else if (result.Decision is null)
            {
                await Send.StatusCodeAsync(StatusCodes.Status202Accepted, ct);
            }
            else
            {
                await Send.OkAsync(result.Decision, ct);
            }
        }
        catch (TenantPurgeApprovalConflictException)
        {
            await Send.StatusCodeAsync(StatusCodes.Status409Conflict, ct);
        }
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/tenants/{tenantId:guid}/purge-approvals");
        Version(0);
    }
}
