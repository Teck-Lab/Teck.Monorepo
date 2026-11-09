using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Events;
using Wolverine;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Signs correlated database readiness using the worker-only deployment secret.</summary>
public sealed class DatabaseReadinessSigningKey
{
    private readonly byte[] key;

    /// <summary>Decodes the deployment-managed, 256-bit-or-stronger base64 key.</summary>
    public DatabaseReadinessSigningKey(string? base64Key)
    {
        if (string.IsNullOrWhiteSpace(base64Key))
            throw new InvalidOperationException("Database readiness signing key is unavailable.");
        try
        {
            key = Convert.FromBase64String(base64Key);
        }
        catch (FormatException error)
        {
            throw new InvalidOperationException("Database readiness signing key is malformed.", error);
        }
        if (key.Length < 32) throw new InvalidOperationException("Database readiness signing key is too short.");
    }

    /// <summary>Attests one persisted CNPG Cluster UID for the confirmed Billing order.</summary>
    public string Sign(TenantProvisioningReadyIntegrationEvent readiness) => TenantReadinessProof.Sign(readiness, key);
}

/// <summary>Uses a scoped Wolverine bus to publish worker readiness over the durable configured transport.</summary>
public sealed class WolverineTenantDatabaseReadinessPublisher(IServiceScopeFactory scopes) : ITenantDatabaseReadinessPublisher
{
    /// <inheritdoc />
    public async Task PublishAsync(TenantProvisioningReadyIntegrationEvent readiness, CancellationToken ct)
    {
        await using AsyncServiceScope scope = scopes.CreateAsyncScope();
        await scope.ServiceProvider.GetRequiredService<IMessageBus>().PublishAsync(readiness).ConfigureAwait(false);
        ct.ThrowIfCancellationRequested();
    }
}
