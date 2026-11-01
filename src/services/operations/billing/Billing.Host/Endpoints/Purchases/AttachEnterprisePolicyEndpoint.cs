using System.Security.Claims;
using Billings.Application.Billing.Purchases;
using Billings.Host.Infrastructure;
using SharedKernel.Infrastructure.Endpoints;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Attaches an exact, validated enterprise policy to an existing paid order.</summary>
/// <param name="handler">The transactional attachment command handler.</param>
public sealed class AttachEnterprisePolicyEndpoint(AttachEnterprisePolicyHandler handler)
    : AuthenticatedEndpoint<AttachEnterprisePolicyRequest, object>
{
    /// <inheritdoc />
    protected override EndpointPermission Permission => new("billing", "purchase", "internal");

    /// <inheritdoc />
    public override async Task HandleAsync(AttachEnterprisePolicyRequest request, CancellationToken ct)
    {
        var subject = HttpContext.User.FindFirst("sub")?.Value ?? HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (string.IsNullOrWhiteSpace(subject))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        try
        {
            var attached = await handler.Handle(new AttachEnterprisePolicyCommand(Route<Guid>("orderId"), request.PolicyId,
                request.PolicyVersion, subject), ct).ConfigureAwait(false);
            if (!attached)
            {
                await Send.StatusCodeAsync(StatusCodes.Status409Conflict, ct);
                return;
            }

            await Send.NoContentAsync(ct);
        }
        catch (Grpc.Core.RpcException)
        {
            await Send.StatusCodeAsync(StatusCodes.Status503ServiceUnavailable, ct);
        }
    }

    /// <inheritdoc />
    protected override void ConfigureEndpoint()
    {
        Put("/purchases/{orderId:guid}/enterprise-policy");
        Version(0);
    }
}
