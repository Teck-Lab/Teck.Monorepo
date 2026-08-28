using Aspire.Hosting.Testing;

namespace Aspire.AppHost.IntegrationTests;

/// <summary>Creates isolated AppHost instances without changing production persistence defaults.</summary>
internal static class AppHostTestBuilder
{
    private const string TestPostgresPassword = "apphost-integration-test-password";
    private const string TestKeycloakAdminPassword = "apphost-integration-test-keycloak-admin-password";

    internal static Task<IDistributedApplicationTestingBuilder> CreateAsync() =>
        DistributedApplicationTestingBuilder.CreateAsync<Projects.Teck_AppHost>([
            $"Parameters:postgres-password={TestPostgresPassword}",
            $"Parameters:keycloak-admin-password={TestKeycloakAdminPassword}",
            "UseVolumes=false",
            "UseFixedKeycloakPort=false",
        ]);
}
