using System.Text.Json;
using Teck.LocalIdentity;
using Xunit;

namespace Teck.LocalIdentity.UnitTests;

/// <summary>Verifies that tenant-registry writes occur only after organization provisioning succeeds.</summary>
public sealed class LocalIdentityProvisionerTests
{
    /// <summary>Ensures an organization failure prevents every tenant-registry write.</summary>
    [Fact]
    public async Task ProvisionAsync_WhenOrganizationProvisioningFails_DoesNotWriteTenantRegistry()
    {
        var writer = new RecordingTenantRegistryWriter();
        var provisioner = new LocalIdentityProvisioner(new FailingOrganizationReconciler(), writer);
        using JsonDocument manifest = JsonDocument.Parse("""{"organizations":[]}""");

        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(manifest, TestContext.Current.CancellationToken));

        Assert.False(writer.WasCalled);
    }

    /// <summary>Ensures a partial organization result prevents every tenant-registry write.</summary>
    [Fact]
    public async Task ProvisionAsync_WhenOrganizationProvisioningIsPartial_DoesNotWriteTenantRegistry()
    {
        var writer = new RecordingTenantRegistryWriter();
        var provisioner = new LocalIdentityProvisioner(new PartialOrganizationReconciler(), writer);
        using JsonDocument manifest = JsonDocument.Parse("""
            {"organizations":[
              {"alias":"teck-local-alpha","tenant":{"identifier":"teck-local-alpha"}},
              {"alias":"teck-local-beta","tenant":{"identifier":"teck-local-beta"}}
            ]}
            """);

        await Assert.ThrowsAsync<InvalidOperationException>(() => provisioner.ProvisionAsync(manifest, TestContext.Current.CancellationToken));

        Assert.False(writer.WasCalled);
    }

    private sealed class FailingOrganizationReconciler : IOrganizationReconciler
    {
        public Task<IReadOnlyList<ProvisionedOrganization>> ReconcileAsync(JsonDocument manifest, CancellationToken cancellationToken) =>
            throw new InvalidOperationException("Keycloak organization provisioning failed.");
    }

    private sealed class PartialOrganizationReconciler : IOrganizationReconciler
    {
        public Task<IReadOnlyList<ProvisionedOrganization>> ReconcileAsync(JsonDocument manifest, CancellationToken cancellationToken) =>
            Task.FromResult<IReadOnlyList<ProvisionedOrganization>>(
            [new ProvisionedOrganization(Guid.NewGuid(), "teck-local-alpha", "teck-local-alpha", "shared", "postgres", false)]);
    }

    private sealed class RecordingTenantRegistryWriter : ITenantRegistryWriter
    {
        internal bool WasCalled { get; private set; }

        public Task UpsertAsync(IReadOnlyList<ProvisionedOrganization> organizations, CancellationToken cancellationToken)
        {
            WasCalled = true;
            return Task.CompletedTask;
        }
    }
}
