using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace SharedKernel.Infrastructure.Testing;

public abstract class ServiceIntegrationTest<TProgram> : IAsyncLifetime
    where TProgram : class
{
    private readonly PostgreSqlContainer _postgresContainer = new PostgreSqlBuilder().Build();

    private readonly RabbitMqContainer _rabbitMqContainer = new RabbitMqBuilder().Build();

    private WebApplicationFactory<TProgram>? _factory;

    protected HttpClient HttpClient { get; private set; } = null!;

    protected IServiceProvider ServiceProvider => _factory?.Services ?? throw new InvalidOperationException("The test host has not been initialized.");

    protected string PostgreSqlConnectionString => _postgresContainer.GetConnectionString();

    protected string RabbitMqConnectionString => _rabbitMqContainer.GetConnectionString();

    public async ValueTask InitializeAsync()
    {
        await _postgresContainer.StartAsync();
        await _rabbitMqContainer.StartAsync();

        _factory = CreateFactory();
        HttpClient = _factory.CreateClient();
    }

    public async ValueTask DisposeAsync()
    {
        HttpClient.Dispose();

        _factory?.Dispose();

        await _rabbitMqContainer.DisposeAsync();
        await _postgresContainer.DisposeAsync();
    }

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
