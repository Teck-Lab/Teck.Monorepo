using System.Net;
using VaultSharp;
using VaultSharp.Core;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Stores only authenticated app-role connections under tenant/service paths in OpenBao KV v2.</summary>
public sealed class OpenBaoTenantConnectionStore(IVaultClient vault, string mount) : ITenantOpenBaoConnectionStore
{
    /// <inheritdoc />
    public async Task WriteAsync(string path, Guid sourceOrderId, Guid previousSourceOrderId, string write, CancellationToken ct)
    {
        ValidatePath(path);
        if (sourceOrderId == Guid.Empty || sourceOrderId == previousSourceOrderId || string.IsNullOrWhiteSpace(write))
            throw new InvalidOperationException("A signed paid tenant, service, and verified application connection are required.");
        ct.ThrowIfCancellationRequested();

        int version;
        try
        {
            var metadata = await vault.V1.Secrets.KeyValue.V2.ReadSecretMetadataAsync(path: path, mountPoint: mount).ConfigureAwait(false);
            version = metadata?.Data?.CurrentVersion ?? throw new InvalidOperationException("Existing OpenBao secret has no current version.");
        }
        catch (VaultApiException error) when (error.HttpStatusCode == HttpStatusCode.NotFound)
        {
            if (previousSourceOrderId != Guid.Empty)
                throw new InvalidOperationException("Renewal cannot replace a missing predecessor OpenBao connection.");
            version = 0;
        }

        if (version != 0)
        {
            try
            {
                var current = await vault.V1.Secrets.KeyValue.V2.ReadSecretAsync(path: path, mountPoint: mount).ConfigureAwait(false);
                var data = current?.Data?.Data ?? throw new InvalidOperationException("OpenBao returned no tenant connection data.");
                string currentOrder = data.TryGetValue("source_order_id", out object? order) ? order?.ToString() ?? "" : "";
                bool suspended = data.TryGetValue("state", out object? state) && state?.ToString() == "suspended";
                if (suspended && currentOrder == sourceOrderId.ToString("D"))
                    throw new InvalidOperationException("A suspended paid order cannot restore its old application credentials.");
                if (currentOrder == sourceOrderId.ToString("D") && data.TryGetValue("write", out object? oldWrite) &&
                    string.Equals(oldWrite?.ToString(), write, StringComparison.Ordinal)) return;
                if (currentOrder != previousSourceOrderId.ToString("D") && currentOrder != sourceOrderId.ToString("D"))
                    throw new InvalidOperationException("OpenBao tenant connection is owned by another paid order.");
            }
            catch (VaultApiException error) when (error.HttpStatusCode == HttpStatusCode.NotFound)
            {
                // A soft-deleted version remains recoverable; only a signed successor can create a fresh version.
                if (previousSourceOrderId == Guid.Empty)
                    throw new InvalidOperationException("A suspended tenant cannot restore its old paid order's connection.");
            }
        }
        ct.ThrowIfCancellationRequested();
        await vault.V1.Secrets.KeyValue.V2.WriteSecretAsync(path: path,
            data: new Dictionary<string, object>
            {
                ["source_order_id"] = sourceOrderId.ToString("D"), ["write"] = write,
            }, checkAndSet: version, mountPoint: mount).ConfigureAwait(false);
    }
    /// <summary>CAS-revokes only the matching paid period and soft-deletes older connection versions.</summary>
    public async Task RevokeAsync(string path, Guid sourceOrderId, CancellationToken ct)
    {
        ValidatePath(path);
        if (sourceOrderId == Guid.Empty) throw new InvalidOperationException("A paid order is required to revoke tenant credentials.");
        ct.ThrowIfCancellationRequested();
        int version;
        try
        {
            var metadata = await vault.V1.Secrets.KeyValue.V2.ReadSecretMetadataAsync(path: path, mountPoint: mount).ConfigureAwait(false);
            version = metadata?.Data?.CurrentVersion ?? throw new InvalidOperationException("Existing OpenBao secret has no current version.");
        }
        catch (VaultApiException error) when (error.HttpStatusCode == HttpStatusCode.NotFound)
        {
            return; // No published application connection existed before suspension.
        }
        var current = await vault.V1.Secrets.KeyValue.V2.ReadSecretAsync(path: path, mountPoint: mount).ConfigureAwait(false);
        var data = current?.Data?.Data ?? throw new InvalidOperationException("OpenBao returned no tenant connection data.");
        if (!data.TryGetValue("source_order_id", out object? owner) || owner?.ToString() != sourceOrderId.ToString("D"))
            throw new InvalidOperationException("A stale suspension cannot revoke another paid order's connection.");
        bool suspended = data.TryGetValue("state", out object? state) && state?.ToString() == "suspended";
        if (!suspended)
        {
            await vault.V1.Secrets.KeyValue.V2.WriteSecretAsync(path: path,
                data: new Dictionary<string, object>
                {
                    ["source_order_id"] = sourceOrderId.ToString("D"), ["state"] = "suspended",
                }, checkAndSet: version, mountPoint: mount).ConfigureAwait(false);
            version++;
        }
        if (version > 1)
            await vault.V1.Secrets.KeyValue.V2.DeleteSecretVersionsAsync(path, Enumerable.Range(1, version - 1).ToList(), mount).ConfigureAwait(false);
    }

    private void ValidatePath(string path)
    {
        string[] parts = path.Split('/');
        if (parts.Length != 3 || parts[0] != "tenants" || string.IsNullOrWhiteSpace(parts[1]) ||
            parts[1] is "." or ".." || !TenantDatabaseReconciler.SupportedServices.Contains(parts[2]) ||
            string.IsNullOrWhiteSpace(mount))
            throw new InvalidOperationException("A tenant and supported service path are required for OpenBao connections.");
    }
}
