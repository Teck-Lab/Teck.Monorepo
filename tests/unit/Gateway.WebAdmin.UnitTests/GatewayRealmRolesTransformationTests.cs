using System.Security.Claims;
using Gateway.WebAdmin;
using Xunit;

namespace Gateway.WebAdmin.UnitTests;

/// <summary>Checks the web-admin authorization bridge from validated realm roles.</summary>
public sealed class GatewayRealmRolesTransformationTests
{
    private readonly GatewayRealmRolesTransformation transformation = new();

    /// <summary>A validated manager token gains the operator role exactly once on repeat transformation.</summary>
    [Fact]
    public async Task AuthenticatedManagerRealmAccess_GrantsOperatorRoleOnlyOnce()
    {
        var identity = new ClaimsIdentity(
            [new Claim("realm_access", "{\"roles\":[\"platform-manager\",\"platform-reader\"]}")],
            "Bearer");
        var principal = new ClaimsPrincipal(identity);

        await transformation.TransformAsync(principal);
        await transformation.TransformAsync(principal);

        Assert.True(principal.IsInRole("platform-manager"));
        Assert.Equal(1, identity.FindAll(identity.RoleClaimType).Count(claim => claim.Value == "platform-manager"));
    }

    /// <summary>Missing or unvalidated roles never confer operator rights.</summary>
    [Theory]
    [InlineData(false, "{\"roles\":[\"platform-manager\"]}")]
    [InlineData(true, "{\"roles\":[\"platform-reader\"]}")]
    [InlineData(true, "{\"roles\":\"platform-manager\"}")]
    public async Task UntrustedOrNonManagerRealmAccess_DoesNotGrantOperatorRole(bool authenticated, string realmAccess)
    {
        var principal = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim("realm_access", realmAccess)], authenticated ? "Bearer" : null));

        await transformation.TransformAsync(principal);

        Assert.False(principal.IsInRole("platform-manager"));
    }
}
