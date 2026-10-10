using System.Net;
using System.Text;
using System.Text.Json;
using Keycloak.AuthServices.Authentication;
using Keycloak.AuthServices.Authorization.AuthorizationServer;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using SharedKernel.Infrastructure.Auth;
using Xunit;

namespace SharedKernel.UnitTests.Auth;

/// <summary>Verifies protected-resource authorization uses Teck's resource-server-aware UMA client.</summary>
public sealed class AuthorizationServerClientRegistrationTests
{
    /// <summary>Replaces the package UMA client with the repository implementation after Keycloak authorization services are registered.</summary>
    [Fact]
    public void AddKeycloak_WhenResolvingAuthorizationServerClient_UsesClientCredentialsImplementation()
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:realm"] = "teck",
                ["Keycloak:auth-server-url"] = "http://localhost:8080",
                ["Keycloak:resource"] = "order-api",
                ["Keycloak:credentials:secret"] = "local-only-order-api-secret-not-for-production",
            })
            .Build();
        KeycloakAuthenticationOptions options = configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!;
        var services = new ServiceCollection();

        services.AddKeycloak(configuration, new TestHostEnvironment(), options);

        Assert.Single(services.Where(descriptor => descriptor.ServiceType == typeof(IAuthorizationServerClient)));

        using ServiceProvider provider = services.BuildServiceProvider();
        IAuthorizationServerClient client = provider.GetRequiredService<IAuthorizationServerClient>();

        Assert.Equal("SharedKernel.Infrastructure.Auth.ClientCredentialsAuthorizationServerClient", client.GetType().FullName);
    }

    /// <summary>Audience selection does not replace requesting-party identity or resource-server credentials.</summary>
    [Theory]
    [InlineData(null, "order-api")]
    [InlineData("catalog-api", "catalog-api")]
    public async Task VerifyAccessToResource_WhenAudienceSelected_PreservesUmaIdentity(string? audience, string expectedAudience)
    {
        IConfiguration configuration = new ConfigurationBuilder()
            .AddInMemoryCollection(new Dictionary<string, string?>
            {
                ["Keycloak:realm"] = "teck",
                ["Keycloak:auth-server-url"] = "http://localhost:8080",
                ["Keycloak:resource"] = "order-api",
                ["Keycloak:credentials:secret"] = "local-only-order-api-secret-not-for-production",
            })
            .Build();
        KeycloakAuthenticationOptions options = configuration.GetSection("Keycloak").Get<KeycloakAuthenticationOptions>()!;
        var handler = new UmaRequestHandler();
        var services = new ServiceCollection();
        services.AddKeycloak(configuration, new TestHostEnvironment(), options);
        services.AddSingleton<IKeycloakAccessTokenProvider>(new TestAccessTokenProvider());
        services.AddSingleton<IHttpClientFactory>(new TestHttpClientFactory(handler));
        using ServiceProvider provider = services.BuildServiceProvider();
        IAuthorizationServerClient client = provider.GetRequiredService<IAuthorizationServerClient>();

        bool allowed = await client.VerifyAccessToResource("orders", "read", ScopesValidationMode.AllOf, audience);

        Assert.True(allowed);
        Assert.Equal(expectedAudience, handler.Form!["audience"]);
        Assert.Equal("requesting-party-token", handler.Form["subject_token"]);
        Assert.Equal("orders#read", handler.Form["permission"]);
        Assert.Equal("decision", handler.Form["response_mode"]);
        Assert.Equal("Basic", handler.AuthenticationScheme);
        Assert.Equal("order-api:local-only-order-api-secret-not-for-production", handler.Credentials);
    }

    private sealed class TestAccessTokenProvider : IKeycloakAccessTokenProvider
    {
        public Task<string?> GetAccessTokenAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult<string?>("requesting-party-token");
    }

    private sealed class TestHttpClientFactory(HttpMessageHandler handler) : IHttpClientFactory
    {
        public HttpClient CreateClient(string name) => new(handler, disposeHandler: false);
    }

    private sealed class UmaRequestHandler : HttpMessageHandler
    {
        public Dictionary<string, string>? Form { get; private set; }

        public string? AuthenticationScheme { get; private set; }

        public string? Credentials { get; private set; }

        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            string body = await request.Content!.ReadAsStringAsync(cancellationToken);
            Form = body.Split('&').Select(pair => pair.Split('=', 2)).ToDictionary(
                pair => WebUtility.UrlDecode(pair[0]), pair => WebUtility.UrlDecode(pair[1]));
            AuthenticationScheme = request.Headers.Authorization!.Scheme;
            Credentials = Encoding.UTF8.GetString(Convert.FromBase64String(request.Headers.Authorization.Parameter!));
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent(JsonSerializer.Serialize(new { result = true })),
            };
        }
    }

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "SharedKernel.UnitTests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
