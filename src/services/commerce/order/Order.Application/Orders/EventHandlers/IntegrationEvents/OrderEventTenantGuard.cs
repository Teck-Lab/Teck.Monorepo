using Finbuckle.MultiTenant.Abstractions;

namespace Orders.Application.Orders.EventHandlers.IntegrationEvents;

/// <summary>Rejects integration events whose untrusted payload tenant differs from the Wolverine envelope tenant.</summary>
internal static class OrderEventTenantGuard
{
    /// <summary>Ensures the payload tenant agrees with the tenant established by middleware from the message envelope.</summary>
    /// <param name="payloadTenantId">The tenant identifier carried by the untrusted event payload.</param>
    /// <param name="tenant">The ambient tenant established from the trusted Wolverine envelope.</param>
    /// <exception cref="InvalidOperationException">Thrown when the event payload and envelope tenants differ.</exception>
    public static void EnsureMatchesEnvelope(string payloadTenantId, ITenantInfo tenant)
    {
        if (!string.Equals(payloadTenantId, tenant.Id, StringComparison.Ordinal))
        {
            throw new InvalidOperationException($"Order integration event payload tenant '{payloadTenantId}' does not match Wolverine envelope tenant '{tenant.Id}'.");
        }
    }
}
