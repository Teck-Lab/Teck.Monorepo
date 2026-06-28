using SharedKernel.Core.Domain;

namespace Customers.Domain.Entities;

/// <summary>The global tenant registry record and authority for per-tenant database strategy.</summary>
public sealed class Tenant : BaseEntity, IAggregateRoot
{
    private Tenant(
        Guid id,
        string identifier,
        string databaseStrategy,
        string databaseProvider,
        bool hasReadReplicas)
    {
        Id = id;
        Identifier = identifier;
        DatabaseStrategy = databaseStrategy;
        DatabaseProvider = databaseProvider;
        HasReadReplicas = hasReadReplicas;
        Status = "active";
    }

    private Tenant()
    {
    }

    /// <summary>Gets the tenant's unique identifier slug.</summary>
    public string Identifier { get; private set; } = string.Empty;

    /// <summary>Gets the tenant's database strategy (e.g. "shared", "dedicated").</summary>
    public string DatabaseStrategy { get; private set; } = string.Empty;

    /// <summary>Gets the tenant's database provider (e.g. "postgres").</summary>
    public string DatabaseProvider { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the tenant has read replicas.</summary>
    public bool HasReadReplicas { get; private set; }

    /// <summary>Gets the tenant status.</summary>
    public string Status { get; private set; } = string.Empty;

    /// <summary>Creates a new tenant registry record.</summary>
    /// <param name="id">The tenant unique identifier.</param>
    /// <param name="identifier">The unique identifier slug.</param>
    /// <param name="databaseStrategy">The database strategy (e.g. "shared", "dedicated").</param>
    /// <param name="databaseProvider">The database provider (e.g. "postgres").</param>
    /// <param name="hasReadReplicas">Whether read replicas are configured for this tenant.</param>
    /// <returns>The created <see cref="Tenant"/>.</returns>
    /// <exception cref="ArgumentException">Thrown when <paramref name="identifier"/> is blank.</exception>
    public static Tenant Create(
        Guid id,
        string identifier,
        string databaseStrategy,
        string databaseProvider,
        bool hasReadReplicas)
    {
        if (string.IsNullOrWhiteSpace(identifier))
        {
            throw new ArgumentException("Identifier must be provided.", nameof(identifier));
        }

        return new Tenant(id, identifier, databaseStrategy, databaseProvider, hasReadReplicas);
    }
}
