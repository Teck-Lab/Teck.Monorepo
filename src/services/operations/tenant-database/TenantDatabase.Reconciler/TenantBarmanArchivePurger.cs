using Amazon.S3;
using Amazon.S3.Model;

namespace Teck.TenantDatabase.Reconciler;

/// <summary>Minimal S3 surface required for destructive Barman archive erasure.</summary>
public interface ITenantBarmanS3Transport
{
    /// <summary>Lists every object version and delete marker for a prefix.</summary>
    Task<ListVersionsResponse> ListVersionsAsync(ListVersionsRequest request, CancellationToken ct);

    /// <summary>Deletes exact object versions and delete markers.</summary>
    Task<DeleteObjectsResponse> DeleteObjectsAsync(DeleteObjectsRequest request, CancellationToken ct);
}

/// <summary>Production adapter over AWS SDK S3.</summary>
public sealed class TenantBarmanS3Transport(IAmazonS3 s3) : ITenantBarmanS3Transport
{
    /// <inheritdoc />
    public Task<ListVersionsResponse> ListVersionsAsync(ListVersionsRequest request, CancellationToken ct) =>
        s3.ListVersionsAsync(request, ct);

    /// <inheritdoc />
    public Task<DeleteObjectsResponse> DeleteObjectsAsync(DeleteObjectsRequest request, CancellationToken ct) =>
        s3.DeleteObjectsAsync(request, ct);
}

/// <summary>Erases every version and delete marker under one tenant's Barman archive prefix.</summary>
public sealed class TenantBarmanArchivePurger(ITenantBarmanS3Transport s3, TenantBarmanStoreOptions options)
{
    /// <summary>Builds the archive prefix with the same canonical UUID-N suffix used by CNPG ObjectStore.</summary>
    public static string GetTenantPrefix(TenantBarmanStoreOptions options, Guid tenantId)
    {
        ArgumentNullException.ThrowIfNull(options);
        if (tenantId == Guid.Empty)
            throw new ArgumentException("Tenant identity must not be empty.", nameof(tenantId));
        options.Validate();
        Uri destination = new(options.DestinationPath, UriKind.Absolute);
        return destination.AbsolutePath.TrimStart('/') + tenantId.ToString("N") + "/";
    }

    /// <summary>Deletes all object versions and markers and confirms the tenant prefix is absent.</summary>
    public async Task PurgeAsync(Guid tenantId, CancellationToken ct)
    {
        ArgumentNullException.ThrowIfNull(s3);
        string prefix = GetTenantPrefix(options, tenantId);
        Uri destination = new(options.DestinationPath, UriKind.Absolute);
        string bucket = destination.Host;
        string? keyMarker = null;
        string? versionMarker = null;

        do
        {
            ListVersionsResponse page = await s3.ListVersionsAsync(new ListVersionsRequest
            {
                BucketName = bucket,
                Prefix = prefix,
                KeyMarker = keyMarker,
                VersionIdMarker = versionMarker,
            }, ct).ConfigureAwait(false);
            if (page.HttpStatusCode != System.Net.HttpStatusCode.OK)
                throw new InvalidOperationException("S3 returned an unsuccessful archive version listing response.");

            List<S3ObjectVersion> versions = page.Versions ?? [];
            if (versions.Any(version => version.Key is null || !version.Key.StartsWith(prefix, StringComparison.Ordinal) ||
                                        string.IsNullOrEmpty(version.VersionId)))
                throw new InvalidOperationException("S3 returned an archive version outside the tenant prefix or without a version ID.");

            List<KeyVersion> objects = versions.Select(version => new KeyVersion
            {
                Key = version.Key,
                VersionId = version.VersionId,
            }).ToList();

            for (int offset = 0; offset < objects.Count; offset += 1000)
            {
                List<KeyVersion> batch = objects.Skip(offset).Take(1000).ToList();
                DeleteObjectsResponse deleted = await s3.DeleteObjectsAsync(new DeleteObjectsRequest
                {
                    BucketName = bucket,
                    Objects = batch,
                    Quiet = true,
                }, ct).ConfigureAwait(false);
                if ((deleted.DeleteErrors?.Count ?? 0) != 0)
                    throw new InvalidOperationException($"S3 refused to delete {deleted.DeleteErrors!.Count} tenant archive object versions.");
            }

            if (page.IsTruncated != true)
                break;
            if (string.IsNullOrEmpty(page.NextKeyMarker))
                throw new InvalidOperationException("S3 returned a truncated archive version listing without a key marker.");
            if (string.Equals(page.NextKeyMarker, keyMarker, StringComparison.Ordinal) &&
                string.Equals(page.NextVersionIdMarker, versionMarker, StringComparison.Ordinal))
                throw new InvalidOperationException("S3 repeated an archive version listing pagination marker.");
            keyMarker = page.NextKeyMarker;
            versionMarker = page.NextVersionIdMarker;
        }
        while (true);

        ListVersionsResponse verification = await s3.ListVersionsAsync(new ListVersionsRequest
        {
            BucketName = bucket,
            Prefix = prefix,
        }, ct).ConfigureAwait(false);
        if (verification.HttpStatusCode != System.Net.HttpStatusCode.OK)
            throw new InvalidOperationException("S3 returned an unsuccessful archive verification response.");
        if ((verification.Versions?.Count ?? 0) != 0 || verification.IsTruncated == true)
            throw new InvalidOperationException("Tenant Barman archive prefix is not empty after deletion.");
    }
}
