namespace Customers.Application.Tenants.ReadModels;

/// <summary>Projection of the tenant fields needed for a database-strategy lookup.</summary>
/// <param name="TenantId">The tenant identifier.</param>
/// <param name="Identifier">The unique slug.</param>
/// <param name="DatabaseStrategy">The database strategy.</param>
/// <param name="DatabaseProvider">The database provider.</param>
/// <param name="HasReadReplicas">Whether read replicas exist.</param>
public sealed record TenantDatabaseInfo(
    Guid TenantId,
    string Identifier,
    string DatabaseStrategy,
    string DatabaseProvider,
    bool HasReadReplicas);
