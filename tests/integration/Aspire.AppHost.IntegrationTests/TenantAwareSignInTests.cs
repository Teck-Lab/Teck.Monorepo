using System.Text.Json;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Verifies that reconciliation is what turns the imported realm into a usable tenant-aware sign-in state.</summary>
[Collection("LocalIdentityKeycloak")]
public sealed class TenantAwareSignInTests(LocalIdentityKeycloakFixture fixture)
{
    private readonly LocalIdentityKeycloakFixture fixture = fixture;

    /// <summary>Validates that both committed local users can complete a password grant immediately after the realm import.</summary>
    [Fact]
    public async Task CommittedUsers_WhenRealmIsFreshlyImported_CanSignInWithoutManualSetup()
    {
        string developerToken = await LocalIdentityKeycloakFixture.GetTokenAsync(
            await fixture.GetUnprovisionedAsync().ConfigureAwait(false),
            LocalIdentityKeycloakFixture.DeveloperUsername,
            LocalIdentityKeycloakFixture.DeveloperPassword).ConfigureAwait(false);
        string readerToken = await LocalIdentityKeycloakFixture.GetTokenAsync(
            await fixture.GetUnprovisionedAsync().ConfigureAwait(false),
            LocalIdentityKeycloakFixture.ReaderUsername,
            LocalIdentityKeycloakFixture.ReaderPassword).ConfigureAwait(false);

        Assert.False(string.IsNullOrWhiteSpace(developerToken));
        Assert.False(string.IsNullOrWhiteSpace(readerToken));
    }

    /// <summary>Validates that a real local-developer token exposes both generated Keycloak organization identifiers.</summary>
    [Fact]
    public async Task DeveloperSignIn_WhenProvisioned_ListsBothOrganizationsWithIdentifiers()
    {
        string accessToken = await LocalIdentityKeycloakFixture.GetTokenAsync(
            await SelectedInstanceAsync().ConfigureAwait(false),
            LocalIdentityKeycloakFixture.DeveloperUsername,
            LocalIdentityKeycloakFixture.DeveloperPassword).ConfigureAwait(false);
        AssertOrganizationMemberships(LocalIdentityKeycloakFixture.ReadToken(accessToken));
    }

    private async Task<LocalIdentityTestInstance> SelectedInstanceAsync() =>
        string.Equals(Environment.GetEnvironmentVariable("TECK_LOCAL_IDENTITY_INSTANCE"), "unprovisioned", StringComparison.Ordinal)
            ? await fixture.GetUnprovisionedAsync().ConfigureAwait(false)
            : fixture.Provisioned;

    internal static void AssertOrganizationMemberships(System.IdentityModel.Tokens.Jwt.JwtSecurityToken token)
    {
        string[] ids = ReadOrganizationIds(token);
        Assert.Equal(2, ids.Length);
        Assert.All(ids, id => Assert.True(Guid.TryParse(id, out _), $"Organization id '{id}' was not a GUID."));
    }

    internal static string[] ReadOrganizationIds(System.IdentityModel.Tokens.Jwt.JwtSecurityToken token)
    {
        return token.Claims
            .Where(item => item.Type == "organization")
            .Select(item => item.Value)
            .Where(value => value.TrimStart().StartsWith('{'))
            .Select(ParseOrganizationIds)
            .Single(ids => ids.Length > 0);
    }

    private static string[] ParseOrganizationIds(string claim)
    {
        using JsonDocument organizations = JsonDocument.Parse(claim);
        Assert.Equal(JsonValueKind.Object, organizations.RootElement.ValueKind);
        return organizations.RootElement.EnumerateObject()
            .Where(organization => organization.Value.TryGetProperty("id", out _))
            .Select(organization => organization.Value.GetProperty("id").GetString())
            .Where(id => !string.IsNullOrWhiteSpace(id))
            .Cast<string>()
            .Order(StringComparer.Ordinal)
            .ToArray();
    }
}
