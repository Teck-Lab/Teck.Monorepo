using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Verifies AppHost wires local identity provisioning to its required dependencies.</summary>
public sealed class LocalIdentityResourceTests
{
    /// <summary>Ensures the local identity command has stable Keycloak configuration and waits for both prerequisites.</summary>
    [Fact]
    public async Task LocalIdentity_WhenAppHostModelIsBuilt_HasStableKeycloakAndRequiredWaits()
    {
        using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Teck_AppHost>([
            "Parameters:postgres-password=apphost-integration-test-password",
            "Parameters:keycloak-admin-password=local-only-keycloak-admin-password-not-for-production",
            "UseVolumes=false",
        ]);

        var keycloak = Assert.IsType<KeycloakResource>(appHost.Resources.Single(resource => resource.Name == "keycloak"));
        Assert.Equal("keycloak-admin-password", keycloak.AdminPasswordParameter.Name);
        Assert.Contains(keycloak.Annotations.OfType<EndpointAnnotation>(), endpoint => endpoint.Port == 8080);

        var localIdentity = appHost.Resources.Single(resource => resource.Name == "local-identity");
        var waits = localIdentity.Annotations.OfType<WaitAnnotation>().Select(annotation => annotation.Resource.Name).ToArray();
        Assert.Contains("keycloak", waits);
        Assert.Contains("customer", waits);
    }

    /// <summary>Ensures AppHost integration tests do not bind the developer-only fixed Keycloak host port.</summary>
    [Fact]
    public async Task AppHostTests_WhenFixedKeycloakPortIsDisabled_UseDynamicEndpoint()
    {
        using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Teck_AppHost>([
            "Parameters:postgres-password=apphost-integration-test-password",
            "Parameters:keycloak-admin-password=local-only-keycloak-admin-password-not-for-production",
            "UseVolumes=false",
            "UseFixedKeycloakPort=false",
        ]);

        var keycloak = Assert.IsType<KeycloakResource>(appHost.Resources.Single(resource => resource.Name == "keycloak"));
        Assert.Contains(keycloak.Annotations.OfType<EndpointAnnotation>(), endpoint => endpoint.Port is null);
    }
}
