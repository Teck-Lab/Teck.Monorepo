using System.Security.Claims;
using Orders.Application.Orders;

namespace Orders.Host.Infrastructure;

/// <summary>Reads the authenticated standard Keycloak <c>sub</c> claim from the current request.</summary>
/// <param name="httpContextAccessor">The HTTP context accessor.</param>
public sealed class OrderIdentityAccessor(IHttpContextAccessor httpContextAccessor) : IOrderIdentityAccessor
{
    /// <inheritdoc/>
    public string? Subject => httpContextAccessor.HttpContext?.User.FindFirstValue("sub");
}
