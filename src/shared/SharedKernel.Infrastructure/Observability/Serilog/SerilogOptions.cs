using SharedKernel.Core.Options;

namespace SharedKernel.Infrastructure.Observability.Serilog;

/// <summary>
/// Options for Serilog configuration. Bound from appsettings.json "Serilog" section.
/// </summary>
public sealed class SerilogOptions : IOptionsRoot
{
    /// <summary>
    /// Gets or sets a value indicating whether Serilog log enrichers are enabled.
    /// </summary>
    public bool EnableEnrichers { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether logging to the console sink is enabled.
    /// </summary>
    public bool EnableConsole { get; set; } = true;

    /// <summary>
    /// Gets or sets a value indicating whether logging to the rolling file sink is enabled.
    /// </summary>
    public bool EnableFile { get; set; } = false;

    /// <summary>
    /// Gets or sets the path template used by the file sink.
    /// </summary>
    public string FilePath { get; set; } = "logs/teck-.log";

    /// <summary>
    /// Gets or sets a value indicating whether logging to the Grafana Loki sink is enabled.
    /// </summary>
    public bool EnableLoki { get; set; } = false;

    /// <summary>
    /// Gets or sets the URL of the Grafana Loki endpoint.
    /// </summary>
    public string LokiUrl { get; set; } = string.Empty;
}
