using System.Net;
using System.Security.Cryptography;
using System.Text;
using Amazon.Runtime;
using Amazon.S3;
using Amazon.S3.Model;
using DotNet.Testcontainers.Builders;
using Xunit;

using Teck.TenantDatabase.Reconciler;
namespace Billing.IntegrationTests;

/// <summary>Exercises a disposable Garage S3 store used for tenant-isolated Barman backup and WAL tests.</summary>
public sealed class TenantGarageObjectStoreTests
{
    [Fact]
    public async Task TenantPrefix_BackupsAndWalCanBeReadAndCompletelyDeletedWithoutTouchingAnotherTenant()
    {
        const string bucket = "testcontainers";
        byte[] rpcSecret = RandomNumberGenerator.GetBytes(32);
        string configuration = $"""
            metadata_dir = "/var/lib/garage/meta"
            data_dir = "/var/lib/garage/data"
            db_engine = "sqlite"
            replication_factor = 1
            rpc_bind_addr = "[::]:3901"
            rpc_public_addr = "127.0.0.1:3901"
            rpc_secret = "{Convert.ToHexString(rpcSecret).ToLowerInvariant()}"

            [s3_api]
            s3_region = "garage"
            api_bind_addr = "[::]:3900"
            """;
        await using var garage = new ContainerBuilder("dxflrs/garage:v2.4.1")
            .WithEntrypoint("/garage")
            .WithCommand("server", "--single-node", "--default-bucket")
            .WithEnvironment("GARAGE_DEFAULT_ACCESS_KEY", "testcontainers")
            .WithEnvironment("GARAGE_DEFAULT_SECRET_KEY", "testcontainers-secret")
            .WithEnvironment("GARAGE_DEFAULT_BUCKET", bucket)
            .WithResourceMapping(Encoding.UTF8.GetBytes(configuration), "/etc/garage.toml")
            .WithPortBinding(3900, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("S3 API server listening"))
            .Build();
        await garage.StartAsync(TestContext.Current.CancellationToken);

        using var s3 = new AmazonS3Client(new BasicAWSCredentials("testcontainers", "testcontainers-secret"),
            new AmazonS3Config
            {
                ServiceURL = $"http://{garage.Hostname}:{garage.GetMappedPublicPort(3900)}",
                AuthenticationRegion = "garage",
                ForcePathStyle = true,
            });
        string own = $"tenant-{Guid.NewGuid():N}/";
        string another = $"tenant-{Guid.NewGuid():N}/";
        var names = new[] { own + "base/backup.tar", own + "wals/000000010000000000000001", another + "base/backup.tar" };
        foreach (string key in names)
        {
            using var data = new MemoryStream(Encoding.UTF8.GetBytes(key));
            var put = await s3.PutObjectAsync(new PutObjectRequest { BucketName = bucket, Key = key, InputStream = data, UseChunkEncoding = false },
                TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.OK, put.HttpStatusCode);
        }

        using (var backup = await s3.GetObjectAsync(bucket, names[0], TestContext.Current.CancellationToken))
        using (var content = new StreamReader(backup.ResponseStream))
            Assert.Equal(names[0], await content.ReadToEndAsync(TestContext.Current.CancellationToken));
        using (var wal = await s3.GetObjectAsync(bucket, names[1], TestContext.Current.CancellationToken))
        using (var content = new StreamReader(wal.ResponseStream))
            Assert.Equal(names[1], await content.ReadToEndAsync(TestContext.Current.CancellationToken));

        foreach (string key in names[..2])
        {
            var delete = await s3.DeleteObjectAsync(bucket, key, TestContext.Current.CancellationToken);
            Assert.Equal(HttpStatusCode.NoContent, delete.HttpStatusCode);
        }
        var ownObjects = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, Prefix = own },
            TestContext.Current.CancellationToken);
        Assert.True(ownObjects.S3Objects is null || ownObjects.S3Objects.Count == 0);
        using var untouched = await s3.GetObjectAsync(bucket, names[2], TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, untouched.HttpStatusCode);
    }
    [Fact]
    public async Task TenantArchivePurger_FailsClosedWhenGarageDoesNotImplementVersionListing()
    {
        const string bucket = "testcontainers";
        byte[] rpcSecret = RandomNumberGenerator.GetBytes(32);
        string configuration = $"""
            metadata_dir = "/var/lib/garage/meta"
            data_dir = "/var/lib/garage/data"
            db_engine = "sqlite"
            replication_factor = 1
            rpc_bind_addr = "[::]:3901"
            rpc_public_addr = "127.0.0.1:3901"
            rpc_secret = "{Convert.ToHexString(rpcSecret).ToLowerInvariant()}"

            [s3_api]
            s3_region = "garage"
            api_bind_addr = "[::]:3900"
            """;
        await using var garage = new ContainerBuilder("dxflrs/garage:v2.4.1")
            .WithEntrypoint("/garage")
            .WithCommand("server", "--single-node", "--default-bucket")
            .WithEnvironment("GARAGE_DEFAULT_ACCESS_KEY", "testcontainers")
            .WithEnvironment("GARAGE_DEFAULT_SECRET_KEY", "testcontainers-secret")
            .WithEnvironment("GARAGE_DEFAULT_BUCKET", bucket)
            .WithResourceMapping(Encoding.UTF8.GetBytes(configuration), "/etc/garage.toml")
            .WithPortBinding(3900, true)
            .WithWaitStrategy(Wait.ForUnixContainer().UntilMessageIsLogged("S3 API server listening"))
            .Build();
        await garage.StartAsync(TestContext.Current.CancellationToken);

        using var s3 = new AmazonS3Client(new BasicAWSCredentials("testcontainers", "testcontainers-secret"),
            new AmazonS3Config
            {
                ServiceURL = $"http://{garage.Hostname}:{garage.GetMappedPublicPort(3900)}",
                AuthenticationRegion = "garage",
                ForcePathStyle = true,
            });
        Guid tenantId = Guid.NewGuid();
        string prefix = TenantBarmanArchivePurger.GetTenantPrefix(
            new TenantBarmanStoreOptions("s3://" + bucket + "/archive/", null, "unused"), tenantId);
        string ownKey = prefix + "base/backup";
        const string neighborKey = "archive/neighbor/base/backup";
        foreach (string key in new[] { ownKey, neighborKey })
        {
            using var body = new MemoryStream(Encoding.UTF8.GetBytes(key));
            await s3.PutObjectAsync(new PutObjectRequest
            {
                BucketName = bucket, Key = key, InputStream = body, UseChunkEncoding = false,
            }, TestContext.Current.CancellationToken);
        }

        var purger = new TenantBarmanArchivePurger(new TenantBarmanS3Transport(s3),
            new TenantBarmanStoreOptions("s3://" + bucket + "/archive/", null, "unused"));
        await Assert.ThrowsAsync<AmazonS3Exception>(() => purger.PurgeAsync(tenantId, TestContext.Current.CancellationToken));

        var ownObjects = await s3.ListObjectsV2Async(new ListObjectsV2Request { BucketName = bucket, Prefix = prefix },
            TestContext.Current.CancellationToken);
        Assert.Single(ownObjects.S3Objects);
        using var neighbor = await s3.GetObjectAsync(bucket, neighborKey, TestContext.Current.CancellationToken);
        Assert.Equal(HttpStatusCode.OK, neighbor.HttpStatusCode);
    }
    [Fact]
    public async Task PurgeAsync_RejectsOffPrefixVersionBeforeDeletingAnyObjects()
    {
        var transport = new RecordingTenantBarmanTransport { IncludeOffPrefixVersion = true };
        Guid tenantId = Guid.NewGuid();
        var purger = new TenantBarmanArchivePurger(transport,
            new TenantBarmanStoreOptions("s3://archive-bucket/backups/", null, "credentials"));

        await Assert.ThrowsAsync<InvalidOperationException>(() => purger.PurgeAsync(tenantId, CancellationToken.None));

        Assert.Equal(0, transport.DeleteCalls);
        Assert.Empty(transport.DeletedObjects);
    }

    [Fact]
    public async Task PurgeAsync_DeletesVersionsAndMarkersAcrossPagesThenConfirmsAbsence()
    {
        var transport = new RecordingTenantBarmanTransport();
        Guid tenantId = Guid.NewGuid();
        var purger = new TenantBarmanArchivePurger(transport,
            new TenantBarmanStoreOptions("s3://archive-bucket/backups/", null, "credentials"));

        await purger.PurgeAsync(tenantId, CancellationToken.None);

        Assert.Equal("backups/" + tenantId.ToString("N") + "/", transport.Prefix);
        Assert.Equal(3, transport.ListCalls);
        Assert.Equal(2, transport.DeletedObjects.Count);
        Assert.Contains(transport.DeletedObjects, item => item.Key == "backups/" + tenantId.ToString("N") + "/key" && item.VersionId == "v1");
        Assert.Contains(transport.DeletedObjects, item => item.Key == "backups/" + tenantId.ToString("N") + "/key" && item.VersionId == "delete-marker");
    }


    [Fact]
    public async Task PurgeAsync_DeletesMoreThanOneThousandVersionsInBoundedBatches()
    {
        var transport = new RecordingTenantBarmanTransport { ManyVersions = 1005 };
        var purger = new TenantBarmanArchivePurger(transport,
            new TenantBarmanStoreOptions("s3://archive-bucket/backups/", null, "credentials"));

        await purger.PurgeAsync(Guid.NewGuid(), CancellationToken.None);

        Assert.Equal(2, transport.DeleteCalls);
        Assert.Equal(1005, transport.DeletedObjects.Count);
        Assert.All(transport.DeleteBatchSizes, size => Assert.InRange(size, 1, 1000));
    }
    [Fact]
    public async Task PurgeAsync_PropagatesListFailureWithoutDeletingObjects()
    {
        var transport = new RecordingTenantBarmanTransport { ListFailure = new AmazonS3Exception("denied") };
        var purger = new TenantBarmanArchivePurger(transport,
            new TenantBarmanStoreOptions("s3://archive-bucket/backups/", null, "credentials"));

        await Assert.ThrowsAsync<AmazonS3Exception>(() => purger.PurgeAsync(Guid.NewGuid(), CancellationToken.None));
        Assert.Equal(0, transport.DeleteCalls);
    }

    private sealed class RecordingTenantBarmanTransport : ITenantBarmanS3Transport
    {
        public string? Prefix { get; private set; }
        public int ListCalls { get; private set; }
        public int DeleteCalls { get; private set; }
        public int? ManyVersions { get; init; }
        public bool IncludeOffPrefixVersion { get; init; }
        public List<int> DeleteBatchSizes { get; } = [];
        public Exception? ListFailure { get; init; }
        public List<KeyVersion> DeletedObjects { get; } = [];

        public Task<ListVersionsResponse> ListVersionsAsync(ListVersionsRequest request, CancellationToken ct)
        {
            if (ListFailure is not null) throw ListFailure;
            Prefix = request.Prefix;
            ListCalls++;
            var response = new ListVersionsResponse { IsTruncated = false, HttpStatusCode = HttpStatusCode.OK };
            if (ManyVersions is int count && ListCalls == 1)
            {
                response.Versions = Enumerable.Range(0, count)
                    .Select(index => new S3ObjectVersion { Key = request.Prefix + index, VersionId = "v1" }).ToList();
            }
            else if (ManyVersions is null && ListCalls == 1)
            {
                if (IncludeOffPrefixVersion)
                {
                    response.Versions =
                    [
                        new S3ObjectVersion { Key = request.Prefix + "own", VersionId = "own-version" },
                        new S3ObjectVersion { Key = request.Prefix.TrimEnd('/') + "-neighbor/object", VersionId = "neighbor-version" },
                    ];
                    return Task.FromResult(response);
                }
                response.IsTruncated = true;
                response.NextKeyMarker = request.Prefix + "key";
                response.NextVersionIdMarker = "v1";
                response.Versions = [new S3ObjectVersion { Key = request.Prefix + "key", VersionId = "v1" }];
            }
            else if (ManyVersions is null && ListCalls == 2)
            {
                response.Versions = [new S3ObjectVersion { Key = request.Prefix + "key", VersionId = "delete-marker", IsDeleteMarker = true }];
            }
            return Task.FromResult(response);
        }

        public Task<DeleteObjectsResponse> DeleteObjectsAsync(DeleteObjectsRequest request, CancellationToken ct)
        {
            DeleteCalls++;
            DeleteBatchSizes.Add(request.Objects.Count);
            DeletedObjects.AddRange(request.Objects);
            return Task.FromResult(new DeleteObjectsResponse());
        }
    }
}
