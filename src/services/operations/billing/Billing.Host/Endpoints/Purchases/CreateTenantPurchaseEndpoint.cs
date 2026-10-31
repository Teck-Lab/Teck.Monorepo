using System.Security.Claims;
using System.Text.Json;
using Billings.Application.Billing.Purchases;
using Billings.Host.Infrastructure;
using SharedKernel.Infrastructure.Endpoints;
using Wolverine;

namespace Billings.Host.Endpoints.Purchases;

/// <summary>Creates an idempotent, server-priced tenant purchase quote.</summary>
/// <param name="bus">The Wolverine message bus.</param>
/// <param name="reservations">Authenticated Customer reservation lookup.</param>
public sealed class CreateTenantPurchaseEndpoint(IMessageBus bus, CustomerReservationClient reservations)
    : AuthenticatedEndpoint<CreateTenantPurchaseRequest, TenantPurchaseOrderDto>
{
    /// <inheritdoc/>
    protected override EndpointPermission Permission => EndpointPermission.ForPolicy("PendingPurchaseBuyer", "billing-api");

    /// <inheritdoc/>
    public override async Task HandleAsync(CreateTenantPurchaseRequest request, CancellationToken ct)
    {
        var isSalesAssisted = HttpContext.User.IsInRole("sales") || HasSalesRealmRole();
        var buyerSubject = HttpContext.User.FindFirst("sub")?.Value ?? HttpContext.User.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (request.ReservationId == Guid.Empty || string.IsNullOrWhiteSpace(buyerSubject))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        var reservation = await reservations.GetReservationAsync(request.ReservationId, ct).ConfigureAwait(false);
        if (reservation is null || reservation.TenantId != request.ReservationId ||
            string.IsNullOrWhiteSpace(reservation.TenantIdentifier) ||
            reservation.Status != "pending" || reservation.PurchaseOrderId != Guid.Empty ||
            (isSalesAssisted
                ? string.IsNullOrWhiteSpace(request.BeneficiarySubjectId) ||
                  !string.Equals(reservation.OwnerSubjectId, request.BeneficiarySubjectId, StringComparison.Ordinal)
                : !string.IsNullOrWhiteSpace(request.BeneficiarySubjectId) ||
                  !string.Equals(reservation.OwnerSubjectId, buyerSubject, StringComparison.Ordinal)))
        {
            await Send.ForbiddenAsync(ct);
            return;
        }

        try
        {
            var dto = await bus.InvokeAsync<TenantPurchaseOrderDto>(new CreateTenantPurchaseCommand(
                reservation.TenantId, reservation.TenantIdentifier, request.PlanId, request.PlanVersion, request.DatabaseProvider,
                request.HasReadReplicas, request.ReplicaCount, buyerSubject, reservation.OwnerSubjectId), ct).ConfigureAwait(false);
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
        Post("/purchases");
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
