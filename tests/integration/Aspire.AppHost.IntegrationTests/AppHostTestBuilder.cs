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
            "Parameters:billing-client-secret=local-only-test-billing-client-secret-not-for-production",
            "Parameters:customer-org-provisioner-secret=local-only-test-customer-org-provisioner-secret-not-for-production",
            "Parameters:stripe-secret-key=sk_test_apphost-isolated-no-network",
            "Parameters:stripe-webhook-secret=whsec_apphost-isolated-no-network",
            "Parameters:billing-payment-private-key=not-a-real-encrypted-payment-key",
            "Parameters:billing-payment-public-key=not-a-real-payment-public-key",
            "Parameters:billing-payment-passphrase=local-only-test-payment-passphrase",
            "Parameters:license-private-key=not-a-real-encrypted-private-key",
            "Parameters:license-public-key=not-a-real-test-public-key",
            "Parameters:license-passphrase=local-only-test-license-passphrase",
            "Parameters:license-readiness-key=bG9jYWwtYXBwaG9zdC1saWNlbnNlLWtleS0zMi1ieXRlcw==",
            "Parameters:database-readiness-key=bG9jYWwtYXBwaG9zdC1kYXRhYmFzZS1rZXktMzItYnl0ZXM=",
            "UseFixedKeycloakPort=false",
            "KeycloakRealmImportPath=./test-realms",
            "UseVolumes=false",
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
