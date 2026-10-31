using Customers.Application.Database;
using Customers.Domain.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;
using SharedKernel.Infrastructure.Endpoints;

namespace Customers.Host.Endpoints.Tenants;

/// <summary>Reads canonical owner-bound tenant reservations for authenticated Billing callers.</summary>
/// <param name="context">The global Customer tenant registry.</param>
public sealed class GetPurchaseReservationEndpoint(CustomerDbContext context)
    : AuthenticatedEndpoint<EmptyRequest, PurchaseReservationResponse>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.ForPolicy("BillingPurchase", "internal");

    /// <inheritdoc/>
    public override async Task HandleAsync(EmptyRequest request, CancellationToken ct)
    {
        var reservationId = Route<Guid>("reservationId");
        Tenant? tenant = await context.Tenants.AsNoTracking()
            .SingleOrDefaultAsync(item => item.Id == reservationId, ct).ConfigureAwait(false);
        if (tenant is null)
        {
            await Send.NotFoundAsync(ct);
            return;
        }

        await Send.OkAsync(new PurchaseReservationResponse
        {
            TenantId = tenant.Id,
            TenantIdentifier = tenant.Identifier,
            OwnerSubjectId = tenant.OwnerSubjectId,
            Status = tenant.Status,
            PurchaseOrderId = tenant.PurchaseOrderId,
        }, ct);
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Get("/internal/purchase-reservations/{reservationId:guid}");
        Version(0);
    }
}
