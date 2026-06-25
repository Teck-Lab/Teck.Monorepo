using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace SharedKernel.Infrastructure.Testing;

/// <summary>
/// Base class for integration tests that host a service against ephemeral PostgreSQL and RabbitMQ containers.
/// </summary>
/// <typeparam name="TProgram">The entry point type of the application under test.</typeparam>
public abstract class ServiceIntegrationTest<TProgram> : IAsyncLifetime
    where TProgram : class
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder().Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder().Build();

    private WebApplicationFactory<TProgram>? _factory;

    /// <summary>
    /// Gets the HTTP client used to send requests to the hosted test application.
    /// </summary>
    protected HttpClient HttpClient { get; private set; } = null!;

    /// <summary>
    /// Gets the service provider of the hosted test application.
    /// </summary>
    protected IServiceProvider ServiceProvider => _factory?.Services ?? throw new InvalidOperationException("The test host has not been initialized.");

    /// <summary>
    /// Gets the connection string for the ephemeral PostgreSQL container.
    /// </summary>
    protected string PostgreSqlConnectionString => _postgresContainer.GetConnectionString();

    /// <summary>
    /// Gets the connection string for the ephemeral RabbitMQ container.
    /// </summary>
    protected string RabbitMqConnectionString => _rabbitMqContainer.GetConnectionString();

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();

        _factory = CreateFactory();
        HttpClient = _factory.CreateClient();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        HttpClient.Dispose();

        _factory?.Dispose();

        await _rabbitMqContainer.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

    /// <summary>
    /// Creates the <see cref="WebApplicationFactory{TProgram}"/> used to host the application under test.
    /// </summary>
    /// <returns>The configured web application factory.</returns>
    protected virtual WebApplicationFactory<TProgram> CreateFactory()
    {
        return new TestWebApplicationFactory(this);
    }

    private sealed class TestWebApplicationFactory(ServiceIntegrationTest<TProgram> owner) : WebApplicationFactory<TProgram>
    {
        protected override void ConfigureWebHost(IWebHostBuilder builder)
        {
            builder.ConfigureAppConfiguration((_, configurationBuilder) =>
            {
                configurationBuilder.AddInMemoryCollection(new Dictionary<string, string?>
                {
                    ["ConnectionStrings:PostgreSql"] = owner.PostgreSqlConnectionString,
                    ["ConnectionStrings:RabbitMq"] = owner.RabbitMqConnectionString,
                });
            });
        }
    }
}
