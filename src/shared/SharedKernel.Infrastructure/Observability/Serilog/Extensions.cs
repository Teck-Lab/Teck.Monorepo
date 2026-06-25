using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Events;
using Serilog.Exceptions;
using Serilog.Exceptions.Core;
using Serilog.Exceptions.EntityFrameworkCore.Destructurers;
using Serilog.Sinks.Grafana.Loki;
using Serilog.Sinks.OpenTelemetry;
using Serilog.Sinks.SystemConsole.Themes;
using SharedKernel.Infrastructure.Options;

namespace SharedKernel.Infrastructure.Observability.Serilog;

internal static class Extensions
{
    internal static IHostApplicationBuilder ConfigureTeckCloudSerilog(this IHostApplicationBuilder builder)
    {
        var serilogOptions = builder.Services.BindValidateReturn<SerilogOptions>(builder.Configuration);
        var appName = builder.Environment.ApplicationName;

        builder.Services.AddSerilog((_, loggerConfiguration) =>
        {
            loggerConfiguration
                .MinimumLevel.Information()
                .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
                .MinimumLevel.Override("System", LogEventLevel.Warning)
                .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
                .Enrich.FromLogContext()
                .Enrich.WithExceptionDetails(new DestructuringOptionsBuilder()
                    .WithDefaultDestructurers()
                    .WithDestructurers([new DbUpdateExceptionDestructurer()]))
                .Enrich.WithCorrelationId()
                .WriteTo.OpenTelemetry(options =>
                {
                    options.IncludedData = IncludedData.TraceIdField
                        | IncludedData.SpanIdField
                        | IncludedData.SpecRequiredResourceAttributes;
                });

            if (serilogOptions.EnableEnrichers)
            {
                loggerConfiguration
                    .Enrich.WithProperty("Application", appName)
                    .Enrich.WithEnvironmentName()
                    .Enrich.WithMachineName()
                    .Enrich.WithProcessId()
                    .Enrich.WithThreadId();
            }

            if (serilogOptions.EnableConsole)
            {
                loggerConfiguration.WriteTo.Console(
                    outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj} {Properties:j}{NewLine}{Exception}",
                    theme: SystemConsoleTheme.Literate);
            }

            if (serilogOptions.EnableLoki && !string.IsNullOrWhiteSpace(serilogOptions.LokiUrl))
            {
                loggerConfiguration.WriteTo.GrafanaLoki(serilogOptions.LokiUrl);
            }
        });

        return builder;
    }
}
