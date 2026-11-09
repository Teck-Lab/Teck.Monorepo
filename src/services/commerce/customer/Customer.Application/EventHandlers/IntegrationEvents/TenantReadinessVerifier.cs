using Microsoft.Extensions.Configuration;
using SharedKernel.Events;

namespace Customers.Application.EventHandlers.IntegrationEvents;

/// <summary>Authenticates license and database readiness with separate deployment-managed issuer keys.</summary>
/// <param name="configuration">The Customer runtime secret configuration.</param>
public sealed class TenantReadinessVerifier(IConfiguration configuration)
{
    private readonly byte[]? licenseKey = Decode(configuration["Readiness:LicenseKey"]);
    private readonly byte[]? databaseKey = Decode(configuration["Readiness:DatabaseKey"]);

    /// <summary>Checks an attestation using only the key for its declared provisioner.</summary>
    /// <param name="readiness">The received correlated readiness payload.</param>
    /// <returns>True only when the actual producer signed the exact evidence and IDs.</returns>
    public bool Verify(TenantProvisioningReadyIntegrationEvent readiness)
    {
        byte[]? key = readiness.Provisioner switch
        {
            "license" => licenseKey,
            "database" => databaseKey,
            _ => null,
        };
        return key is not null && TenantReadinessProof.Verify(readiness, key);
    }

    private static byte[]? Decode(string? configured)
    {
        if (string.IsNullOrWhiteSpace(configured))
        {
            return null;
        }

        try
        {
            var key = Convert.FromBase64String(configured);
            return key.Length >= 32 ? key : null;
        }
        catch (FormatException)
        {
            return null;
        }
    }
}
