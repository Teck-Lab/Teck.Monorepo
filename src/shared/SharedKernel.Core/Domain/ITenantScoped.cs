namespace SharedKernel.Core.Domain;

/// <summary>
/// Represents an entity that belongs to a tenant.
/// </summary>
public interface ITenantScoped
{
    /// <summary>
    /// Gets or sets the tenant identifier.
    /// </summary>
    string TenantId { get; set; }
}
