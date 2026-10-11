using System.Net;
using Keycloak.AuthServices.Authorization.AuthorizationServer;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Infrastructure.Auth;
using Xunit;

namespace SharedKernel.UnitTests.Auth;

public sealed class ClientCredentialsAuthorizationServerClientTests
{
    [Fact]
    public async Task VerifyAccessToResource_WhenAudienceProvided_UsesAudienceAndCurrentUserToken()
    {
        var handler = new RecordingHandler();
        IHttpClientFactory httpClientFactory = Substitute.For<IHttpClientFactory>();
        httpClientFactory.CreateClient(string.Empty).Returns(_ => new HttpClient(handler));
        IKeycloakAccessTokenProvider tokenProvider = Substitute.For<IKeycloakAccessTokenProvider>();
        tokenProvider.GetAccessTokenAsync(Arg.Any<CancellationToken>())
            .Returns(Task.FromResult<string?>("requesting-party-token"));
        var options = Options.Create(new KeycloakAuthorizationServerOptions
        {
            Resource = "default-resource-server",
            AuthServerUrl = "https://keycloak.example.test/",
            Realm = "teck",
            Credentials = new() { Secret = "test-secret" },
        });
        IAuthorizationServerClient client = new ClientCredentialsAuthorizationServerClient(
            httpClientFactory,
            tokenProvider,
            options,
            NullLogger<ClientCredentialsAuthorizationServerClient>.Instance);

        bool allowed = await client.VerifyAccessToResource(
            "orders", "read", ScopesValidationMode.AllOf, "target-resource-server");

        Assert.True(allowed);
        Assert.Contains("audience=target-resource-server", handler.RequestBody);
        Assert.Contains("subject_token=requesting-party-token", handler.RequestBody);
        Assert.DoesNotContain("subject_token=target-resource-server", handler.RequestBody);
    }

    private sealed class RecordingHandler : HttpMessageHandler
    {
        public string RequestBody { get; private set; } = string.Empty;

        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            RequestBody = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"result\":true}"),
            };
        }
    }
}
