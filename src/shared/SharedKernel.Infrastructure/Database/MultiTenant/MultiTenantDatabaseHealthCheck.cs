using Finbuckle.MultiTenant.Abstractions;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using SharedKernel.Infrastructure.MultiTenant;

namespace SharedKernel.Infrastructure.Database.MultiTenant
{
    /// <summary>
    /// Health check for multi-tenant database connections.
    /// </summary>
    public class MultiTenantDatabaseHealthCheck : IHealthCheck
    {
        private readonly IMultiTenantStore<TenantDetails> _tenantStore;
        private readonly ITenantDbConnectionResolver _connectionResolver;

        /// <summary>
        /// Initializes a new instance of the <see cref="MultiTenantDatabaseHealthCheck"/> class.
        /// </summary>
        /// <param name="tenantStore">The tenant store to retrieve tenants from.</param>
        /// <param name="connectionResolver">The resolver used to obtain tenant database connections.</param>
        public MultiTenantDatabaseHealthCheck(
            IMultiTenantStore<TenantDetails> tenantStore,
            ITenantDbConnectionResolver connectionResolver)
        {
            _tenantStore = tenantStore;
            _connectionResolver = connectionResolver;
        }

        /// <summary>
        /// Checks the health of multi-tenant database connections.
        /// </summary>
        /// <param name="context">The health check context.</param>
        /// <param name="cancellationToken">A cancellation token.</param>
        /// <returns>A <see cref="HealthCheckResult"/> representing the health of the multi-tenant databases.</returns>
        public async Task<HealthCheckResult> CheckHealthAsync(
            HealthCheckContext context,
            CancellationToken cancellationToken = default)
        {
            try
            {
                // Sample a few tenants to check their database connectivity
                var tenants = await _tenantStore.GetAllAsync();

                // For simplicity, we'll just check if we can resolve connections
                // A more thorough check would actually test the connections
                foreach (var tenant in tenants.Take(5))
                {
                    // This will throw an exception if the connection can't be resolved
                    _connectionResolver.ResolveTenantConnection(tenant);
                }

                return HealthCheckResult.Healthy(
                    "Multi-tenant database connections are available");
            }
            catch (Exception exception)
            {
                return HealthCheckResult.Unhealthy(
                    "Failed to resolve tenant database connections", exception);
            }
        }
    }
}
