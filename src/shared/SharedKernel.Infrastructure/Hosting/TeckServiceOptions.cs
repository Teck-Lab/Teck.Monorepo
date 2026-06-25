namespace SharedKernel.Infrastructure.Hosting;

/// <summary>
/// Options that configure the shared Teck service host behaviour.
/// </summary>
public sealed class TeckServiceOptions
{
    /// <summary>
    /// The configuration section name that binds to these options.
    /// </summary>
    public const string SectionName = "TeckService";

    /// <summary>
    /// Gets the name of the CORS policy registered for the service.
    /// </summary>
    public string CorsPolicyName { get; init; } = "TeckServiceCors";

    /// <summary>
    /// Gets the list of allowed CORS origins. When empty, any origin is allowed.
    /// </summary>
    public string[] CorsOrigins { get; init; } = [];

    /// <summary>
    /// Gets the path that exposes the liveness health check endpoint.
    /// </summary>
    public string HealthPath { get; init; } = "/health";

    /// <summary>
    /// Gets the path that exposes the readiness health check endpoint.
    /// </summary>
    public string ReadyPath { get; init; } = "/ready";
}
