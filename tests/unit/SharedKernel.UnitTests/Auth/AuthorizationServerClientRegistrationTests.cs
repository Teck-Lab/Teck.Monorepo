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

    private sealed class TestHostEnvironment : IHostEnvironment
    {
        public string ApplicationName { get; set; } = "SharedKernel.UnitTests";

        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();

        public string ContentRootPath { get; set; } = Directory.GetCurrentDirectory();

        public string EnvironmentName { get; set; } = Environments.Development;
    }
}
