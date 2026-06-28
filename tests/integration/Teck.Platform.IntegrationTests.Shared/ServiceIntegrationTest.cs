// <copyright file="ServiceIntegrationTest.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Extensions.Configuration;
using Testcontainers.PostgreSql;
using Testcontainers.RabbitMq;
using Xunit;

namespace Teck.Platform.IntegrationTests.Shared;

/// <summary>
/// Base class for integration tests that host a service against ephemeral PostgreSQL and RabbitMQ containers.
/// </summary>
/// <typeparam name="TProgram">The entry point type of the application under test.</typeparam>
public abstract class ServiceIntegrationTest<TProgram> : IAsyncLifetime
    where TProgram : class
{
    private readonly PostgreSqlContainer postgresContainer = new PostgreSqlBuilder().Build();

    private readonly RabbitMqContainer rabbitMqContainer = new RabbitMqBuilder().Build();

    private WebApplicationFactory<TProgram>? factory;

    /// <summary>
    /// Gets the HTTP client used to send requests to the hosted test application.
    /// </summary>
    protected HttpClient HttpClient { get; private set; } = null!;

    /// <summary>
    /// Gets the service provider of the hosted test application.
    /// </summary>
    protected IServiceProvider ServiceProvider => factory?.Services ?? throw new InvalidOperationException("The test host has not been initialized.");

    /// <summary>
    /// Gets the connection string for the ephemeral PostgreSQL container.
    /// </summary>
    protected string PostgreSqlConnectionString => postgresContainer.GetConnectionString();

    /// <summary>
    /// Gets the connection string for the ephemeral RabbitMQ container.
    /// </summary>
    protected string RabbitMqConnectionString => rabbitMqContainer.GetConnectionString();

    /// <inheritdoc/>
    public async ValueTask InitializeAsync()
    {
        await postgresContainer.StartAsync();
        await rabbitMqContainer.StartAsync();

        factory = CreateFactory();
        HttpClient = factory.CreateClient();
    }

    /// <inheritdoc/>
    public async ValueTask DisposeAsync()
    {
        HttpClient.Dispose();

        factory?.Dispose();

        await rabbitMqContainer.DisposeAsync();
        await postgresContainer.DisposeAsync();
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
