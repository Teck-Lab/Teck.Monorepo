using System.Security.Claims;
using SharedKernel.Infrastructure.MultiTenant;
using Xunit;

namespace SharedKernel.UnitTests.MultiTenant;

/// <summary>Verifies tenant extraction from the organization claims emitted by Keycloak.</summary>
public sealed class TenantTokenContextResolverTests
{
    [Fact]
    public void ResolveTenantIds_WhenScalarOrganizationClaimsPrecedeMembershipObject_ReturnsOnlyMembershipIdentifiers()
    {
        const string alphaTenantId = "11111111-1111-1111-1111-111111111111";
        const string betaTenantId = "22222222-2222-2222-2222-222222222222";
        var user = new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim("organization", "teck-local-alpha"),
            new Claim("organization", "teck-local-beta"),
            new Claim("organization", $"{{\"teck-local-alpha\":{{\"id\":\"{alphaTenantId}\"}},\"teck-local-beta\":{{\"id\":\"{betaTenantId}\"}}}}"),
        ], "test"));

        IReadOnlyList<string> tenantIds = new TenantTokenContextResolver().ResolveTenantIds(user, "organization", "tenant_id");

        Assert.Equal([alphaTenantId, betaTenantId], tenantIds.Order(StringComparer.Ordinal));
    }
}
