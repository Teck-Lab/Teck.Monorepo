using Customers.Application.Database;
using Customers.Domain.Entities;
using Microsoft.EntityFrameworkCore;

namespace Teck.LocalIdentity;

/// <summary>Writes local Keycloak organization registrations through the Customer tenant aggregate.</summary>
public sealed class TenantRegistryWriter : ITenantRegistryWriter
{
    private readonly DbContextOptions<CustomerDbContext> options;

    /// <summary>Initializes a writer with Customer database options.</summary>
    /// <param name="options">The options used to create the Customer write context.</param>
    public TenantRegistryWriter(DbContextOptions<CustomerDbContext> options)
    {
        ArgumentNullException.ThrowIfNull(options);
        this.options = options;
    }

    /// <summary>Creates a writer that connects to the Customer write database.</summary>
    /// <param name="connectionString">The Customer write connection string.</param>
    /// <returns>A writer configured for the supplied connection string.</returns>
    public static TenantRegistryWriter Create(string connectionString)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(connectionString);
        DbContextOptions<CustomerDbContext> options = new DbContextOptionsBuilder<CustomerDbContext>()
            .UseNpgsql(connectionString)
            .Options;
        return new TenantRegistryWriter(options);
    }

    /// <inheritdoc/>
    public async Task UpsertAsync(IReadOnlyList<ProvisionedOrganization> organizations, CancellationToken cancellationToken)
    {
        ArgumentNullException.ThrowIfNull(organizations);
        await using var database = new CustomerDbContext(options, null!);
        foreach (ProvisionedOrganization organization in organizations)
        {
            Tenant? existing = await database.Tenants.SingleOrDefaultAsync(tenant => tenant.Id == organization.Id, cancellationToken).ConfigureAwait(false);
            if (existing is null)
            {
                database.Tenants.Add(Tenant.Create(
                    organization.Id,
                    organization.TenantIdentifier,
                    organization.DatabaseStrategy,
                    organization.DatabaseProvider,
                    organization.HasReadReplicas));
                continue;
            }

            EnsureMatches(existing, organization);
        }

        await database.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
    }

    private static void EnsureMatches(Tenant tenant, ProvisionedOrganization organization)
    {
        if (!string.Equals(tenant.Identifier, organization.TenantIdentifier, StringComparison.Ordinal) ||
            !string.Equals(tenant.DatabaseStrategy, organization.DatabaseStrategy, StringComparison.Ordinal) ||
            !string.Equals(tenant.DatabaseProvider, organization.DatabaseProvider, StringComparison.Ordinal) ||
            tenant.HasReadReplicas != organization.HasReadReplicas)
        {
            throw new InvalidOperationException($"Tenant registry record '{organization.Id}' conflicts with the committed local organization manifest.");
        }
    }
}
