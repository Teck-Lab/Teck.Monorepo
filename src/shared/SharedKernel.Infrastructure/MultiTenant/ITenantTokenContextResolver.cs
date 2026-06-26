using System.Security.Claims;
using System.Text.Json;

namespace SharedKernel.Infrastructure.MultiTenant;

/// <summary>
/// Resolves tenant identifiers from authenticated token claims.
/// </summary>
public interface ITenantTokenContextResolver
{
    /// <summary>
    /// Resolves all available tenant identifiers for the current principal.
    /// </summary>
    /// <param name="user">The authenticated user principal.</param>
    /// <param name="organizationClaimName">The claim name containing organization JSON data.</param>
    /// <param name="tenantIdClaimName">The claim name containing a single tenant identifier.</param>
    /// <returns>A distinct, case-insensitive list of tenant identifiers.</returns>
    IReadOnlyList<string> ResolveTenantIds(
        ClaimsPrincipal user,
        string organizationClaimName,
        string tenantIdClaimName);
}

/// <summary>
/// Default implementation that reads tenant identifiers from organization and tenant-id claims.
/// </summary>
public sealed class TenantTokenContextResolver : ITenantTokenContextResolver
{
    /// <inheritdoc />
    public IReadOnlyList<string> ResolveTenantIds(
        ClaimsPrincipal user,
        string organizationClaimName,
        string tenantIdClaimName)
    {
        ArgumentNullException.ThrowIfNull(user);

        var tenantIds = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        string? organizationClaimValue = user.FindFirst(organizationClaimName)?.Value;
        if (!string.IsNullOrWhiteSpace(organizationClaimValue))
        {
            try
            {
                using JsonDocument organizationsJson = JsonDocument.Parse(organizationClaimValue);
                if (organizationsJson.RootElement.ValueKind == JsonValueKind.Object)
                {
                    foreach (JsonProperty organization in organizationsJson.RootElement.EnumerateObject())
                    {
                        if (!organization.Value.TryGetProperty("id", out JsonElement idElement))
                        {
                            continue;
                        }

                        if (idElement.ValueKind != JsonValueKind.String)
                        {
                            continue;
                        }

                        string? id = idElement.GetString();
                        if (!string.IsNullOrWhiteSpace(id))
                        {
                            tenantIds.Add(id);
                        }
                    }
                }
            }
            catch (JsonException)
            {
                foreach (string tenantId in organizationClaimValue
                    .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
                    .Where(static tenantId => !string.IsNullOrWhiteSpace(tenantId)))
                {
                    tenantIds.Add(tenantId);
                }
            }
        }

        string? singleTenantId = user.FindFirst(tenantIdClaimName)?.Value;
        if (!string.IsNullOrWhiteSpace(singleTenantId))
        {
            tenantIds.Add(singleTenantId);
        }

        return tenantIds.ToList();
    }
}
