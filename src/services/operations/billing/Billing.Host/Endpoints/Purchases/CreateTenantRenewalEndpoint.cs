using System.Security.Claims;
using System.Text.Json;
using Billings.Application.Billing.Purchases;
using Billings.Host.Infrastructure;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Creates one separately payable annual order for the owner of an existing paid tenant.</summary>
/// <param name="bus">Billing purchase command dispatcher.</param>
/// <param name="reservations">Authenticated Customer reservation lookup.</param>
public sealed class CreateTenantRenewalEndpoint(IMessageBus bus, CustomerReservationClient reservations)
    : AuthenticatedEndpoint<CreateTenantPurchaseRequest, TenantPurchaseOrderDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.ForPolicy("PendingPurchaseBuyer", "billing-api");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreateTenantPurchaseRequest request, CancellationToken ct)
    {
        var actor = HttpContext.User.FindFirst("sub")?.Value ?? HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (request.ReservationId == Guid.Empty || string.IsNullOrWhiteSpace(actor))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var reservation = await reservations.GetReservationAsync(request.ReservationId, ct).ConfigureAwait(false);
        var sales = HttpContext.User.IsInRole("sales") || HasSalesRealmRole();
        if (reservation is null || reservation.TenantId != request.ReservationId ||
            string.IsNullOrWhiteSpace(reservation.TenantIdentifier) || reservation.PurchaseOrderId == Guid.Empty ||
            reservation.Status is not ("active" or "suspended") ||
            (sales ? string.IsNullOrWhiteSpace(request.BeneficiarySubjectId) ||
                     !string.Equals(reservation.OwnerSubjectId, request.BeneficiarySubjectId, StringComparison.Ordinal)
                   : !string.IsNullOrWhiteSpace(request.BeneficiarySubjectId) ||
                     !string.Equals(reservation.OwnerSubjectId, actor, StringComparison.Ordinal)))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        try
        {
            var dto = await bus.InvokeAsync<TenantPurchaseOrderDto>(new CreateTenantPurchaseCommand(
                reservation.TenantId, reservation.TenantIdentifier, request.PlanId, request.PlanVersion,
                request.DatabaseProvider, request.HasReadReplicas, request.ReplicaCount,
                actor, reservation.OwnerSubjectId, IsRenewal: true, reservation.PurchaseOrderId), ct).ConfigureAwait(false);
            HttpContext.Response.Headers.Location = $"/purchases/{dto.Id}";
            await Send.ResponseAsync(dto, StatusCodes.Status201Created, ct);
        }
        catch (ArgumentException)
        {
            await Send.ErrorsAsync(StatusCodes.Status400BadRequest, ct);
        }
        catch (InvalidOperationException)
        {
            await Send.StatusCodeAsync(StatusCodes.Status409Conflict, ct);
        }
    }

    /// <inheritdoc/>
    protected override void ConfigureEndpoint()
    {
        Post("/purchases/renewals");
        Version(0);
    }

    private bool HasSalesRealmRole()
    {
        var realmAccess = HttpContext.User.FindFirst("realm_access")?.Value;
        if (string.IsNullOrWhiteSpace(realmAccess))
        {
            return false;
        }

        try
        {
            using var parsed = JsonDocument.Parse(realmAccess);
            return parsed.RootElement.TryGetProperty("roles", out var roles) &&
                roles.ValueKind == JsonValueKind.Array &&
                roles.EnumerateArray().Any(role => role.GetString() == "sales");
        }
        catch (JsonException)
        {
            return false;
        }
    }
}
