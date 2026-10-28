using Customers.Application.Database;
using Customers.Domain.Entities;
using FastEndpoints;
using Microsoft.EntityFrameworkCore;

namespace Customers.Host.Endpoints.Tenants;

/// <summary>Reads canonical owner-bound tenant reservations for authenticated Billing callers.</summary>
/// <param name="context">The global Customer tenant registry.</param>
public sealed class GetPurchaseReservationEndpoint(CustomerDbContext context)
    : EndpointWithoutRequest<PurchaseReservationResponse>
{
    /// <inheritdoc/>
    public override void Configure()
    {
        Get("/internal/purchase-reservations/{reservationId:guid}");
        AuthSchemes("Bearer");
        Policies("BillingPurchase");
        Version(0);
    }

    /// <inheritdoc/>
    public override async Task HandleAsync(CancellationToken ct)
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
}
