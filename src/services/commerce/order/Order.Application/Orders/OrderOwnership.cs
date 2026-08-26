using Orders.Domain.Entities;

namespace Orders.Application.Orders;

/// <summary>Applies object-level authorization for owner-only order payment retry.</summary>
public static class OrderOwnership
{
    /// <summary>Throws unless the current authenticated subject owns the order.</summary>
    /// <param name="order">The loaded tenant-filtered order.</param>
    /// <param name="identity">The current HTTP identity accessor.</param>
    public static void EnsureOwnedBy(Order order, IOrderIdentityAccessor identity)
    {
        ArgumentNullException.ThrowIfNull(order);
        ArgumentNullException.ThrowIfNull(identity);
        if (string.IsNullOrWhiteSpace(identity.Subject) || !string.Equals(order.KeycloakSubjectId, identity.Subject, StringComparison.Ordinal))
        {
            throw new UnauthorizedAccessException($"Order '{order.Id}' is not owned by the current caller.");
        }
    }
}
