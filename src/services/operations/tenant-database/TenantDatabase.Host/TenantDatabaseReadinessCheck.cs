using Microsoft.Extensions.Diagnostics.HealthChecks;
using Npgsql;
using Teck.TenantDatabase.Reconciler;

/// <summary>Readiness requires durable storage and every credential needed to reconcile a paid tenant.</summary>
/// <param name="dataSource">Durable worker request queue.</param>
/// <param name="configuration">Required external service and credential configuration.</param>
/// <param name="environment">Current environment used to enforce production broker and CA requirements.</param>
public sealed class TenantDatabaseReadinessCheck(NpgsqlDataSource dataSource, IConfiguration configuration,
    IHostEnvironment environment) : IHealthCheck
{
    /// <inheritdoc />
    public async Task<HealthCheckResult> CheckHealthAsync(HealthCheckContext context, CancellationToken cancellationToken = default)
    {
        if (TenantDatabaseReconciler.SupportedServices.Any(service =>
                string.IsNullOrWhiteSpace(configuration[$"TenantDatabase:ServiceImages:{service}"])) ||
            string.IsNullOrWhiteSpace(configuration["Keycloak:realm"]) ||
            string.IsNullOrWhiteSpace(configuration["Keycloak:auth-server-url"]) ||
            string.IsNullOrWhiteSpace(configuration["OpenBao:Url"]) ||
            (string.IsNullOrWhiteSpace(configuration["OpenBao:Role"]) &&
             string.IsNullOrWhiteSpace(configuration["OpenBao:Token"])) ||
            string.IsNullOrWhiteSpace(configuration["Readiness:DatabaseKey"]) ||
            string.IsNullOrWhiteSpace(configuration["PurgeApproval:TrustedPublicKeys:0"]) ||
            string.IsNullOrWhiteSpace(configuration["BillingPurgeFenceClient:TokenEndpoint"]) ||
            string.IsNullOrWhiteSpace(configuration["BillingPurgeFenceClient:ClientId"]) ||
            string.Equals(configuration["BillingPurgeFenceClient:ClientId"], "billing-api", StringComparison.Ordinal) ||
            string.IsNullOrWhiteSpace(configuration["BillingPurgeFenceClient:ClientSecret"]) ||
            configuration["BillingPurgeFenceClient:Scope"]?.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                .Contains("billing:purge-fence", StringComparer.Ordinal) != true ||
            string.IsNullOrWhiteSpace(configuration["BillingPurgeFenceClient:BillingBaseUrl"]) ||
            string.IsNullOrWhiteSpace(configuration["TenantDatabase:VolumeSnapshotClass"]))
        {
            return HealthCheckResult.Unhealthy("Tenant database migration, identity, OpenBao, signing, verified-backup or dedicated Billing purge-fence settings are missing.");
        }

        if (!environment.IsDevelopment() &&
            (string.IsNullOrWhiteSpace(configuration["ConnectionStrings:rabbitmq"]) ||
             string.IsNullOrWhiteSpace(configuration["Postgres:RootCertificate"]) ||
             !File.Exists(configuration["Postgres:RootCertificate"])))
        {
            return HealthCheckResult.Unhealthy("Production broker and tenant PostgreSQL CA must be configured.");
        }

        try
        {
            await using var connection = await dataSource.OpenConnectionAsync(cancellationToken);
            await using var command = connection.CreateCommand();
            command.CommandText = "SELECT to_regclass('public.tenant_database_requests') IS NOT NULL";
            return (bool)(await command.ExecuteScalarAsync(cancellationToken) ?? false) ?
                HealthCheckResult.Healthy() : HealthCheckResult.Unhealthy("Tenant database request queue has not been migrated.");
        }
        catch (Exception error) when (error is not OperationCanceledException)
        {
            return HealthCheckResult.Unhealthy("Tenant database request queue is unavailable.", error);
        }
    }
}
