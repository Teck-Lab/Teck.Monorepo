using Finbuckle.MultiTenant.Abstractions;

namespace SharedKernel.Infrastructure.MultiTenant
{
    /// <summary>
    /// Represents tenant details from the Customer API.
    /// </summary>
    public class TenantDetails : ITenantInfo
    {
        /// <summary>
        /// Gets or sets the unique identifier of the tenant.
        /// </summary>
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the identifier.
        /// </summary>
        public string Identifier { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the name of the tenant.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the plan associated with the tenant.
        /// </summary>
        public string Plan { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the database strategy used by the tenant.
        /// </summary>
        public string DatabaseStrategy { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets the write connection string for the tenant's database.
        /// </summary>
        public string? WriteConnectionString { get; set; }

        /// <summary>
        /// Gets or sets the read connection string for the tenant's database.
        /// </summary>
        public string? ReadConnectionString { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the tenant is active.
        /// </summary>
        public bool IsActive { get; set; }

        /// <summary>
        /// Gets or sets a value indicating whether the tenant has read replicas.
        /// </summary>
        public bool HasReadReplicas { get; set; }

        /// <summary>
        /// Gets or sets the database provider used by the tenant.
        /// </summary>
        public string DatabaseProvider { get; set; } = string.Empty;

        /// <summary>
        /// Gets or sets a value indicating whether the tenant is the primary tenant.
        /// </summary>
        public bool IsPrimary { get; set; }
    }
}
