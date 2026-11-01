using Billings.Host.Infrastructure;
using SharedKernel.Infrastructure.Endpoints;

namespace Billings.Host.Endpoints.PurgeFences;

/// <summary>Acquires the Billing gate before worker-side external purge effects begin.</summary>
/// <param name="fence">The durable Billing tenant purge fence.</param>
public sealed class AcquireBillingPurgeFenceEndpoint(BillingPurgeFence fence)
    : AuthenticatedEndpoint<AcquireBillingPurgeFenceRequest, AcquireBillingPurgeFenceResponse>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.ForPolicy("BillingPurgeFence", "billing-api");

    /// <inheritdoc/>
    public override async Task HandleAsync(AcquireBillingPurgeFenceRequest request, CancellationToken ct)
    {
        var grantId = await fence.TryAcquireAsync(Route<Guid>("tenantId"), request.SubscriptionId,
            request.SourceOrderId, request.ApprovalId, ct).ConfigureAwait(false);
        if (grantId is null)
        {
            await Send.ResponseAsync(new AcquireBillingPurgeFenceResponse(Guid.Empty), 409, ct).ConfigureAwait(false);
            return;
        }

        await Send.OkAsync(new AcquireBillingPurgeFenceResponse(grantId.Value), ct).ConfigureAwait(false);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/internal/purge-fences/{tenantId:guid}/acquire");
        Version(0);
    }
}
