using System.Net;
using System.Text;
using Microsoft.Extensions.Options;
using Teck.TenantDatabase.Reconciler;
using Xunit;

namespace Teck.TenantDatabase.Reconciler.UnitTests;

public sealed class BillingPurgeFenceClientTests
{
    [Fact]
    public async Task TryAcquireAsync_UsesDedicatedClientCredentialsAndReturnsDurableGrant()
    {
        var grantId = Guid.NewGuid();
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/token") return Json(HttpStatusCode.OK, "{\"access_token\":\"worker-token\"}");
            return request.Headers.Authorization?.Parameter == "worker-token"
                ? Json(HttpStatusCode.OK, $"{{\"grantId\":\"{grantId:D}\"}}")
                : new HttpResponseMessage(HttpStatusCode.Unauthorized);
        });
        var client = CreateClient(handler);
        var tenantId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var sourceOrderId = Guid.NewGuid();
        var approvalId = Guid.NewGuid();

        Guid? acquired = await client.TryAcquireAsync(tenantId, subscriptionId, sourceOrderId, approvalId, CancellationToken.None);

        Assert.Equal(grantId, acquired);
        Assert.Equal("client_credentials", handler.TokenForm["grant_type"]);
        Assert.Equal("tenant-database-worker", handler.TokenForm["client_id"]);
        Assert.Equal("worker-secret", handler.TokenForm["client_secret"]);
        Assert.Equal("billing:purge-fence", handler.TokenForm["scope"]);
        Assert.Contains($"/{tenantId:D}/acquire", handler.AcquirePath, StringComparison.Ordinal);
        Assert.Contains($"\"subscriptionId\":\"{subscriptionId:D}\"", handler.AcquireBody, StringComparison.Ordinal);
        Assert.Contains($"\"sourceOrderId\":\"{sourceOrderId:D}\"", handler.AcquireBody, StringComparison.Ordinal);
        Assert.Contains($"\"approvalId\":\"{approvalId:D}\"", handler.AcquireBody, StringComparison.Ordinal);
    }

    [Fact]
    public async Task TryAcquireAsync_ReturnsNullOnBillingDenialOrMalformedSuccess()
    {
        var calls = 0;
        var handler = new RecordingHandler((request, _) =>
        {
            if (request.RequestUri!.AbsolutePath == "/token") return Json(HttpStatusCode.OK, "{\"access_token\":\"token\"}");
            return new HttpResponseMessage(++calls == 1 ? HttpStatusCode.Conflict : HttpStatusCode.OK)
            {
                Content = new StringContent("{}", Encoding.UTF8, "application/json"),
            };
        });
        var client = CreateClient(handler);

        Assert.Null(await client.TryAcquireAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
        Assert.Null(await client.TryAcquireAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task CompleteAsync_UsesBearerTokenAndRequiresNoContentResponse()
    {
        var handler = new RecordingHandler((request, _) => request.RequestUri!.AbsolutePath == "/token"
            ? Json(HttpStatusCode.OK, "{\"access_token\":\"worker-token\"}")
            : new HttpResponseMessage(request.Headers.Authorization?.Parameter == "worker-token"
                ? HttpStatusCode.NoContent : HttpStatusCode.Unauthorized));
        var client = CreateClient(handler);

        Assert.True(await client.CompleteAsync(Guid.NewGuid(), CancellationToken.None));
    }
    [Fact]
    public async Task TryAcquireAsync_ReturnsNullWhenBillingUnavailableOrConfigurationMissing()
    {
        var unavailable = CreateClient(new RecordingHandler((_, _) => throw new HttpRequestException("Billing unavailable.")));
        await Assert.ThrowsAsync<HttpRequestException>(() => unavailable.TryAcquireAsync(Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), Guid.NewGuid(), CancellationToken.None));

        var missingOptions = Options.Create(new BillingPurgeFenceClientOptions());
        var missingConfiguration = new BillingPurgeFenceClient(new HttpClient(new RecordingHandler((_, _) =>
            throw new InvalidOperationException("No HTTP request should occur."))), missingOptions);
        Assert.Null(await missingConfiguration.TryAcquireAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(),
            Guid.NewGuid(), CancellationToken.None));
        Assert.False(await missingConfiguration.CompleteAsync(Guid.NewGuid(), CancellationToken.None));
    }
    private static BillingPurgeFenceClient CreateClient(RecordingHandler handler) => new(new HttpClient(handler),
        Options.Create(new BillingPurgeFenceClientOptions
        {
            TokenEndpoint = "https://identity.example/token",
            ClientId = "tenant-database-worker",
            ClientSecret = "worker-secret",
            Scope = "billing:purge-fence",
            BillingBaseUrl = "http://billing-api/",
        }));

    private static HttpResponseMessage Json(HttpStatusCode status, string body) => new(status)
    {
        Content = new StringContent(body, Encoding.UTF8, "application/json"),
    };

    private sealed class RecordingHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> response) : HttpMessageHandler
    {
        public Dictionary<string, string> TokenForm { get; } = new(StringComparer.Ordinal);
        public string AcquirePath { get; private set; } = string.Empty;
        public string AcquireBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            if (request.RequestUri!.AbsolutePath == "/token" && request.Content is not null)
            {
                foreach (string pair in (await request.Content.ReadAsStringAsync(cancellationToken)).Split('&'))
                {
                    string[] parts = pair.Split('=', 2);
                    TokenForm[Uri.UnescapeDataString(parts[0])] = Uri.UnescapeDataString(parts[1].Replace('+', ' '));
                }
            }
            else if (request.RequestUri.AbsolutePath.EndsWith("/acquire", StringComparison.Ordinal))
            {
                AcquirePath = request.RequestUri.AbsolutePath;
                AcquireBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            }

            return response(request, cancellationToken);
        }
    }
}
