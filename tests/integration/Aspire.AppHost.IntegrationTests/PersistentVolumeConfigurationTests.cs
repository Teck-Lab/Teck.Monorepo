using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;
using Xunit;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Protects the production default that stateful resources retain their data volumes.</summary>
public sealed class PersistentVolumeConfigurationTests
{
    [Fact]
    public async Task StatefulResources_UsePersistentDataVolumesByDefault()
    {
        using var appHost = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Teck_AppHost>();

        foreach (string resourceName in new[] { "postgres", "keycloak" })
        {
            var resource = appHost.Resources.Single(candidate => candidate.Name == resourceName);
            Assert.NotEmpty(resource.Annotations.OfType<ContainerMountAnnotation>());
        }
    }
}
