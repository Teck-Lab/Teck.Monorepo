using Billings.Host.Infrastructure;
using FastEndpoints;
using SharedKernel.Infrastructure.Endpoints;

namespace Billings.Host.Endpoints.PurgeFences;

/// <summary>Completes a Billing purge grant only after external deletion and reconciliation succeeded.</summary>
/// <param name="fence">The durable Billing tenant purge fence.</param>
public sealed class CompleteBillingPurgeFenceEndpoint(BillingPurgeFence fence)
    : AuthenticatedEndpoint<EmptyRequest, EmptyResponse>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.ForPolicy("BillingPurgeFence", "billing-api");

    /// <inheritdoc/>
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var grantId = Route<Guid>("grantId");
        if (!await fence.CompleteAsync(Route<Guid>("grantId"), ct).ConfigureAwait(false))
        {
            await Send.NotFoundAsync(ct).ConfigureAwait(false);
            return;
        }

        await Send.NoContentAsync(ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/internal/purge-fences/{grantId:guid}/complete");
        Version(0);
    }
}
