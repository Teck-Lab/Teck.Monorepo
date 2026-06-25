using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Npgsql;
using OpenTelemetry;
using OpenTelemetry.Context.Propagation;
using OpenTelemetry.Logs;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace SharedKernel.Infrastructure.Observability.OpenTelemetry;

internal static class Extensions
{
    internal static IHostApplicationBuilder ConfigureTeckCloudOpenTelemetry(this IHostApplicationBuilder builder)
    {
        var compositeTextMapPropagator = new CompositeTextMapPropagator(
            new TextMapPropagator[]
            {
                new TraceContextPropagator(),
                new BaggagePropagator(),
            });

        Sdk.SetDefaultTextMapPropagator(compositeTextMapPropagator);
        ResourceBuilder resourceBuilder = ResourceBuilder.CreateDefault()
            .AddService(
                serviceName: builder.Environment.ApplicationName,
                serviceVersion: typeof(Extensions).Assembly.GetName().Version?.ToString() ?? "1.0.0",
                serviceInstanceId: Environment.MachineName)
            .AddOperatingSystemDetector()
            .AddContainerDetector()
            .AddHostDetector()
            .AddProcessRuntimeDetector()
            .AddProcessDetector();

        builder.Services.AddOpenTelemetry()
            .WithMetrics(metrics =>
            {
                metrics
                    .SetResourceBuilder(resourceBuilder)
                    .AddAspNetCoreInstrumentation()
                    .AddHttpClientInstrumentation()
                    .AddNpgsqlInstrumentation()
                    .AddRuntimeInstrumentation()
                    .AddFusionCacheInstrumentation()
                    .AddMeter($"Wolverine:{builder.Environment.ApplicationName}")
                    .AddKeycloakAuthServicesInstrumentation()
                    .AddMeter("Microsoft.AspNetCore.Hosting")
                    .AddMeter("Microsoft.AspNetCore.Server.Kestrel")
                    .AddMeter("System.Net.Http")
                    .AddMeter("System.Net.NameResolution")
                    .AddMeter(builder.Environment.ApplicationName)
                    .AddProcessInstrumentation();
            })
            .WithTracing(tracing =>
            {
                // Probabilistic sampling: 100% in dev, 10% in production
                Sampler sampler = builder.Environment.IsDevelopment()
                    ? new AlwaysOnSampler()
                    : new TraceIdRatioBasedSampler(0.1);

                tracing
                    .SetResourceBuilder(resourceBuilder)
                    .SetSampler(sampler)
                    .AddAspNetCoreInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddHttpClientInstrumentation(options =>
                    {
                        options.RecordException = true;
                    })
                    .AddFusionCacheInstrumentation()
                    .AddEntityFrameworkCoreInstrumentation()
                    .AddSource("Wolverine")
                    .AddSource("Yarp.ReverseProxy")
                    .AddKeycloakAuthServicesInstrumentation()
                    .AddRabbitMQInstrumentation()
                    .AddRedisInstrumentation()
                    .AddGrpcClientInstrumentation();
            });

        builder.AddOpenTelemetryExporters();

        return builder;
    }

    private static IHostApplicationBuilder AddOpenTelemetryExporters(this IHostApplicationBuilder builder)
    {
        bool useOtlpExporter = !string.IsNullOrWhiteSpace(builder.Configuration["OTEL_EXPORTER_OTLP_ENDPOINT"]);

        if (useOtlpExporter)
        {
            builder.Services.Configure<OpenTelemetryLoggerOptions>(logging => logging.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryMeterProvider(metrics => metrics.AddOtlpExporter());
            builder.Services.ConfigureOpenTelemetryTracerProvider(tracing => tracing.AddOtlpExporter());
        }

        return builder;
    }
}
