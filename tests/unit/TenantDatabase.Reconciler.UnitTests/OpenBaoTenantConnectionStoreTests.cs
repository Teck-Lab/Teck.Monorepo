using System.Net;
using System.Text;
using System.Text.Json;
using VaultSharp;
using VaultSharp.V1.AuthMethods.Token;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Teck.TenantDatabase.Reconciler.UnitTests;

public sealed class OpenBaoTenantConnectionStoreTests
{
    [Fact]
    public async Task NewPaidPeriod_UsesKvV2CasAndOnlyReplacesItsSignedPredecessor()
    {
        var transport = new KvTransport();
        var settings = new VaultClientSettings("https://openbao.test", new TokenAuthMethodInfo("local-test-token"))
        {
            MyHttpClientProviderFunc = _ => new HttpClient(transport),
        };
        var store = new OpenBaoTenantConnectionStore(new VaultClient(settings), "teck-cloud");
        const string path = "tenants/paid-business/basket";
        var first = Guid.NewGuid();
        var renewal = Guid.NewGuid();

        await store.WriteAsync(path, first, Guid.Empty, "Host=postgres;Username=first", CancellationToken.None);
        Assert.Equal(1, transport.Version);
        Assert.Equal(0, Assert.Single(transport.CasWrites));
        await store.WriteAsync(path, first, Guid.Empty, "Host=postgres;Username=first", CancellationToken.None);
        Assert.Single(transport.CasWrites);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(path, Guid.NewGuid(), Guid.NewGuid(),
            "Host=postgres;Username=stale", CancellationToken.None));
        Assert.Single(transport.CasWrites);

        await store.WriteAsync(path, renewal, first, "Host=postgres;Username=renewed", CancellationToken.None);
        Assert.Equal(2, transport.Version);
        Assert.Equal([0, 1], transport.CasWrites);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(path, first, Guid.Empty,
            "Host=postgres;Username=first", CancellationToken.None));
        Assert.Equal(2, transport.Version);
    }

    [Fact]
    public async Task SuspendedPeriod_RemovesLatestConnectionRejectsStaleWriterAndAcceptsOnlySignedSuccessor()
    {
        var transport = new KvTransport();
        var settings = new VaultClientSettings("https://openbao.test", new TokenAuthMethodInfo("local-test-token"))
        {
            MyHttpClientProviderFunc = _ => new HttpClient(transport),
        };
        var store = new OpenBaoTenantConnectionStore(new VaultClient(settings), "teck-cloud");
        const string path = "tenants/paid-business/basket";
        Guid first = Guid.NewGuid(), next = Guid.NewGuid();
        await store.WriteAsync(path, first, Guid.Empty, "Host=postgres;Username=first", CancellationToken.None);

        await store.RevokeAsync(path, first, CancellationToken.None);

        Assert.Null(transport.CurrentConnection);
        Assert.Equal(first.ToString("D"), transport.CurrentSourceOrder);
        Assert.Equal(2, transport.Version);
        await store.RevokeAsync(path, first, CancellationToken.None);
        Assert.Equal(2, transport.Version);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(path, first, Guid.Empty,
            "Host=postgres;Username=first", CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.WriteAsync(path, next, Guid.NewGuid(),
            "Host=postgres;Username=forged", CancellationToken.None));
        await store.WriteAsync(path, next, first, "Host=postgres;Username=next", CancellationToken.None);
        Assert.Equal("Host=postgres;Username=next", transport.CurrentConnection);
        await Assert.ThrowsAsync<InvalidOperationException>(() => store.RevokeAsync(path, first, CancellationToken.None));
        Assert.Equal("Host=postgres;Username=next", transport.CurrentConnection);
    }

    private sealed class KvTransport : HttpMessageHandler
    {
        public int Version { get; private set; }
        public List<int> CasWrites { get; } = [];
        public string? CurrentConnection => data?.GetValueOrDefault("write");
        public string? CurrentSourceOrder => data?.GetValueOrDefault("source_order_id");
        private Dictionary<string, string>? data;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken ct)
        {
            string path = request.RequestUri!.AbsolutePath;
            if (path.Contains("/metadata/", StringComparison.Ordinal))
                return Version == 0 ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = Body("{\"errors\":[]}") } :
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = Body(JsonSerializer.Serialize(new { data = new { current_version = Version } })) };
            if (request.Method == HttpMethod.Post && path.Contains("/delete/", StringComparison.Ordinal))
            {
                using var body = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                Assert.Contains(body.RootElement.GetProperty("versions").EnumerateArray(), version => version.GetInt32() == 1);
                return new HttpResponseMessage(HttpStatusCode.NoContent);
            }
            if (request.Method == HttpMethod.Get && path.Contains("/data/", StringComparison.Ordinal))
                return data is null ? new HttpResponseMessage(HttpStatusCode.NotFound) { Content = Body("{\"errors\":[]}") } :
                    new HttpResponseMessage(HttpStatusCode.OK) { Content = Body(JsonSerializer.Serialize(new { data = new { data, metadata = new { version = Version } } })) };
            if (request.Method == HttpMethod.Post && path.Contains("/data/", StringComparison.Ordinal))
            {
                using var json = JsonDocument.Parse(await request.Content!.ReadAsStringAsync(ct));
                int cas = json.RootElement.GetProperty("options").GetProperty("cas").GetInt32();
                CasWrites.Add(cas);
                if (cas != Version) return new HttpResponseMessage(HttpStatusCode.BadRequest) { Content = Body("{\"errors\":[\"cas mismatch\"]}") };
                data = json.RootElement.GetProperty("data").EnumerateObject().ToDictionary(property => property.Name,
                    property => property.Value.GetString()!, StringComparer.Ordinal);
                Version++;
                return new HttpResponseMessage(HttpStatusCode.OK) { Content = Body(JsonSerializer.Serialize(new { data = new { version = Version } })) };
            }
            throw new InvalidOperationException("Unexpected OpenBao endpoint: " + path);
        }
        private static StringContent Body(string json) => new(json, Encoding.UTF8, "application/json");
    }
}
