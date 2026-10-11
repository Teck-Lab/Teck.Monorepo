using Aspire.Hosting.ApplicationModel;
using Aspire.Hosting.Testing;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Creates isolated AppHost instances without changing production persistence defaults.</summary>
internal static class AppHostTestBuilder
{
    private const string TestPostgresPassword = "apphost-integration-test-password";
    private const string TestKeycloakAdminPassword = "apphost-integration-test-keycloak-admin-password";

    internal static async Task<IDistributedApplicationTestingBuilder> CreateAsync()
    {
        var builder = await DistributedApplicationTestingBuilder.CreateAsync<Projects.Teck_AppHost>([
            $"Parameters:postgres-password={TestPostgresPassword}",
            $"Parameters:keycloak-admin-password={TestKeycloakAdminPassword}",
            "UseVolumes=false",
            "UseFixedKeycloakPort=false",
        ]).ConfigureAwait(false);

        // The test launches project binaries directly, without the production image's WolverineCodegenWrite step.
        // Keep production's static-codegen contract; use runtime generation only for these test processes.
        foreach (var resource in builder.Resources.OfType<ProjectResource>())
        {
            resource.Annotations.Add(new EnvironmentCallbackAnnotation("DOTNET_ENVIRONMENT", () => "Development"));
            resource.Annotations.Add(new EnvironmentCallbackAnnotation("ASPNETCORE_ENVIRONMENT", () => "Development"));
        }

        return builder;
    }
}
