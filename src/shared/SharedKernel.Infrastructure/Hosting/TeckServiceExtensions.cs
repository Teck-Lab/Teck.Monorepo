using System.Reflection;
using FastEndpoints;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using SharedKernel.Infrastructure.Middlewares;
using SharedKernel.Infrastructure.Resilience;

namespace SharedKernel.Infrastructure.Hosting;

public sealed class TeckServiceOptions
{
    public const string SectionName = "TeckService";

    public string CorsPolicyName { get; init; } = "TeckServiceCors";

    public string[] CorsOrigins { get; init; } = [];

    public string HealthPath { get; init; } = "/health";

    public string ReadyPath { get; init; } = "/ready";
}

public static class TeckServiceExtensions
{
    public static IServiceCollection AddTeckService(
        this IServiceCollection services,
        Assembly assembly,
        IConfiguration configuration)
    {
        TeckServiceOptions options = configuration.GetSection(TeckServiceOptions.SectionName).Get<TeckServiceOptions>() ?? new TeckServiceOptions();

        services.Configure<TeckServiceOptions>(configuration.GetSection(TeckServiceOptions.SectionName));
        services.Configure<TenantRateLimitOptions>(configuration.GetSection(TenantRateLimitOptions.SectionName));
        services.AddFastEndpoints(endpointOptions => endpointOptions.Assemblies = [assembly]);
        services.AddHealthChecks();
        services.AddSingleton<ResiliencePolicies>();

        services.AddCors(corsOptions =>
        {
            corsOptions.AddPolicy(options.CorsPolicyName, policyBuilder =>
            {
                policyBuilder.AllowAnyHeader().AllowAnyMethod();

                if (options.CorsOrigins.Length == 0)
                {
                    policyBuilder.AllowAnyOrigin();
                    return;
                }

                policyBuilder.WithOrigins(options.CorsOrigins).AllowCredentials();
            });
        });

        services.AddResponseCompression();
        services.Configure<HostOptions>(opts => opts.ShutdownTimeout = TimeSpan.FromSeconds(30));

        return services;
    }

    public static WebApplication UseTeckService(this WebApplication app)
    {
        TeckServiceOptions options = app.Services.GetRequiredService<IOptions<TeckServiceOptions>>().Value;

        app.UseSerilogRequestLogging();

        app.Use(async (context, next) =>
        {
            context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            context.Response.Headers["X-Frame-Options"] = "DENY";
            context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
            await next();
        });

        app.UseResponseCompression();
        app.UseCors(options.CorsPolicyName);
        app.UseMiddleware<TenantRateLimitMiddleware>();
        app.UseAuthentication();
        app.UseAuthorization();
        app.UseFastEndpoints();

        app.MapHealthChecks(options.HealthPath);
        app.MapHealthChecks(options.ReadyPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready", StringComparer.OrdinalIgnoreCase),
        });

        return app;
    }
}
