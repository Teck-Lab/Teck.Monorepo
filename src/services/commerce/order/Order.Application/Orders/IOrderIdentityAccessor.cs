namespace Orders.Application.Orders;

/// <summary>Provides the authenticated standard Keycloak subject for order ownership checks.</summary>
public interface IOrderIdentityAccessor
{
    /// <summary>Gets the current standard subject claim, or null when it is absent.</summary>
    string? Subject { get; }
}
