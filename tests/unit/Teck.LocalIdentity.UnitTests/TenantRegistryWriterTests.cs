using Customers.Application.Database;
using Microsoft.EntityFrameworkCore;
using Teck.LocalIdentity;
using Xunit;

namespace Teck.LocalIdentity.UnitTests;

/// <summary>Verifies tenant-registry records are keyed by Keycloak-generated organization identifiers.</summary>
public sealed class TenantRegistryWriterTests
{
    /// <summary>Ensures two generated organization identifiers produce exactly two idempotent tenant rows.</summary>
    [Fact]
    public async Task UpsertAsync_WhenOrganizationsAreReconciled_CreatesExactlyOneTenantPerGeneratedIdentifier()
    {
        DbContextOptions<CustomerDbContext> options = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseInMemoryDatabase($"local-identity-{Guid.NewGuid()}")
            .Options;
        var writer = new TenantRegistryWriter(options);
        IReadOnlyList<ProvisionedOrganization> organizations =
        [
            new(Guid.NewGuid(), "teck-local-alpha", "teck-local-alpha", "shared", "postgres", false),
            new(Guid.NewGuid(), "teck-local-beta", "teck-local-beta", "shared", "postgres", false),
        ];

        await writer.UpsertAsync(organizations, TestContext.Current.CancellationToken).ConfigureAwait(false);
        await writer.UpsertAsync(organizations, TestContext.Current.CancellationToken).ConfigureAwait(false);

        using var database = new CustomerDbContext(options, null!);
        var tenants = await database.Tenants.OrderBy(tenant => tenant.Identifier).ToArrayAsync(TestContext.Current.CancellationToken).ConfigureAwait(false);
        Assert.Equal(2, tenants.Length);
        Assert.Equal(organizations.Select(organization => organization.Id).Order().ToArray(), tenants.Select(tenant => tenant.Id).Order().ToArray());
        Assert.Equal(["teck-local-alpha", "teck-local-beta"], tenants.Select(tenant => tenant.Identifier).ToArray());
        Assert.All(tenants, tenant =>
        {
            Assert.Equal("shared", tenant.DatabaseStrategy);
            Assert.Equal("postgres", tenant.DatabaseProvider);
            Assert.False(tenant.HasReadReplicas);
        });
    }
}
