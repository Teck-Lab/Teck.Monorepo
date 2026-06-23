using Microsoft.Extensions.Hosting;
using SharedKernel.Infrastructure.Observability.OpenTelemetry;
using SharedKernel.Infrastructure.Observability.Serilog;
namespace SharedKernel.Infrastructure.Observability;

/// <summary>
/// Centralized observability setup: OpenTelemetry (tracing + metrics) + Serilog (logging).
/// Called once per service in Program.cs: <c>builder.AddTeckCloudObservability();</c>
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Adds standardized OpenTelemetry observability wiring.
    /// </summary>
    /// <param name="builder">The host builder.</param>
    /// <returns>The same host builder.</returns>
    public static IHostApplicationBuilder AddTeckCloudObservability(this IHostApplicationBuilder builder)
    {
        builder.ConfigureTeckCloudOpenTelemetry();
        builder.ConfigureTeckCloudSerilog();
        return builder;
    }
}
