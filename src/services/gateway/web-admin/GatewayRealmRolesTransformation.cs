using System.Security.Claims;
using System.Text.Json;
using Microsoft.AspNetCore.Authentication;

namespace Gateway.WebAdmin;

/// <summary>Maps only validated Keycloak realm roles into ASP.NET Core role claims.</summary>
public sealed class GatewayRealmRolesTransformation : IClaimsTransformation
{
    /// <inheritdoc />
    public Task<ClaimsPrincipal> TransformAsync(ClaimsPrincipal principal)
    {
        if (principal.Identity is not ClaimsIdentity { IsAuthenticated: true } identity)
        {
            return Task.FromResult(principal);
        }

        string? realmAccess = principal.FindFirst("realm_access")?.Value;
        if (realmAccess is null)
        {
            return Task.FromResult(principal);
        }

        using JsonDocument document = JsonDocument.Parse(realmAccess);
        if (!document.RootElement.TryGetProperty("roles", out JsonElement roles) || roles.ValueKind != JsonValueKind.Array)
        {
            return Task.FromResult(principal);
        }

        foreach (JsonElement role in roles.EnumerateArray())
        {
            if (role.ValueKind == JsonValueKind.String && role.GetString() is { Length: > 0 } name &&
                !principal.IsInRole(name))
            {
                identity.AddClaim(new Claim(identity.RoleClaimType, name));
            }
        }

        return Task.FromResult(principal);
    }
}
