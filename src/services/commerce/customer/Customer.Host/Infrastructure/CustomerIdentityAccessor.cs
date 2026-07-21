using System.Security.Claims;
using Customers.Application.Customers;

namespace Customers.Host.Infrastructure;

/// <summary>
/// Resolves customer identity from the HTTP context: the authenticated Keycloak <c>sub</c> claim.
/// </summary>
/// <param name="httpContextAccessor">The HTTP context accessor.</param>
public sealed class CustomerIdentityAccessor(IHttpContextAccessor httpContextAccessor) : ICustomerIdentityAccessor
{
    /// <inheritdoc/>
    public string? KeycloakSubjectId
    {
        get
        {
            var context = httpContextAccessor.HttpContext;
            if (context is null)
            {
                return null;
            }

            return context.User?.FindFirstValue("sub") ?? context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        }
    }
}
