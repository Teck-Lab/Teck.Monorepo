using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using SharedKernel.Infrastructure.Observability;

namespace Teck.ServiceDefaults;

/// <summary>
/// Aspire service defaults for Teck hosts: composes the existing Serilog + OpenTelemetry
/// observability and adds Aspire service discovery and standard HTTP resilience.
/// </summary>
public static class TeckServiceDefaultsExtensions
{
    /// <summary>
    /// Adds the Teck service defaults: rich observability (via <c>AddTeckCloudObservability</c>),
    /// service discovery, and standard HTTP resilience for all <see cref="System.Net.Http.HttpClient"/>s.
    /// </summary>
    /// <param name="builder">The host application builder.</param>
    /// <returns>The same builder for chaining.</returns>
    public static IHostApplicationBuilder AddServiceDefaults(this IHostApplicationBuilder builder)
    {
        ArgumentNullException.ThrowIfNull(builder);

        builder.AddTeckCloudObservability();

        builder.Services.AddServiceDiscovery();
        builder.Services.ConfigureHttpClientDefaults(http =>
        {
            http.AddStandardResilienceHandler();
            http.AddServiceDiscovery();
        });

        // Aspire liveness convention, in addition to the existing /health and /ready from AddTeckService.
        builder.Services.AddHealthChecks()
            .AddCheck("self", () => HealthCheckResult.Healthy(), ["live"]);

        return builder;
    }

    /// <summary>
    /// Maps the Aspire liveness endpoint <c>/alive</c> (checks tagged <c>live</c>). The existing
    /// <c>/health</c> and <c>/ready</c> endpoints are mapped by <c>UseTeckService</c>.
    /// </summary>
    /// <param name="app">The web application.</param>
    /// <returns>The same application for chaining.</returns>
    public static WebApplication MapDefaultEndpoints(this WebApplication app)
    {
        ArgumentNullException.ThrowIfNull(app);

        app.MapHealthChecks("/alive", new()
        {
            Predicate = registration => registration.Tags.Contains("live"),
        });

        return app;
    }
}
