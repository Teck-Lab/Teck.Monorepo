using SharedKernel.Core.Options;

namespace SharedKernel.Infrastructure.Observability.Serilog;

/// <summary>
/// Options for Serilog configuration. Bound from appsettings.json "Serilog" section.
/// </summary>
public sealed class SerilogOptions : IOptionsRoot
{
    public bool EnableEnrichers { get; set; } = true;
    public bool EnableConsole { get; set; } = true;
    public bool EnableFile { get; set; } = false;
    public string FilePath { get; set; } = "logs/teck-.log";
    public bool EnableLoki { get; set; } = false;
    public string LokiUrl { get; set; } = string.Empty;
}
