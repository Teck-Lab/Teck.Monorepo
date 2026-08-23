using System.Reflection;
using FastEndpoints;
using JasperFx;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;
using Serilog;
using SharedKernel.Infrastructure.Middlewares;
using SharedKernel.Infrastructure.Resilience;

namespace SharedKernel.Infrastructure.Hosting;

/// <summary>
/// Extension methods that register and configure the shared Teck service host pipeline.
/// </summary>
public static class TeckServiceExtensions
{
    /// <summary>
    /// Registers the shared Teck service dependencies (options, endpoints, health checks, CORS and resilience).
    /// </summary>
    /// <param name="services">The service collection to add registrations to.</param>
    /// <param name="assembly">The assembly scanned for FastEndpoints endpoints.</param>
    /// <param name="configuration">The application configuration used to bind options.</param>
    /// <returns>The same service collection so calls can be chained.</returns>
    public static IServiceCollection AddTeckService(
        this IServiceCollection services,
        Assembly assembly,
        IConfiguration configuration)
    {
        TeckServiceOptions options = configuration.GetSection(TeckServiceOptions.SectionName).Get<TeckServiceOptions>() ?? new TeckServiceOptions();

        services.Configure<TeckServiceOptions>(configuration.GetSection(TeckServiceOptions.SectionName));
        services.Configure<TenantRateLimitOptions>(configuration.GetSection(TenantRateLimitOptions.SectionName));

        // Services that act as handler-only hosts (e.g. Customer.Host with only gRPC remote
        // handlers) have no HTTP endpoint declarations. FastEndpoints throws in that case, so we
        // pre-check: if the assembly contains no concrete IEndpoint types, register a marker
        // singleton and skip AddFastEndpoints. UseTeckService checks the same marker to skip
        // UseFastEndpoints, which would fail if the FE services were never registered.
        if (assembly.GetTypes().Any(static t =>
                t is { IsAbstract: false, IsInterface: false }
                && typeof(IEndpoint).IsAssignableFrom(t)))
        {
            services.AddFastEndpoints(endpointOptions => endpointOptions.Assemblies = [assembly]);
        }
        else
        {
            services.AddSingleton<NoHttpEndpointsMarker>();
        }

        services.AddHealthChecks();
        services.AddAuthentication();
        services.AddAuthorization();
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

    /// <summary>
    /// Configures the shared Teck service middleware pipeline (logging, security headers, CORS, auth, endpoints and health checks).
    /// </summary>
    /// <param name="app">The web application to configure.</param>
    /// <returns>The same web application so calls can be chained.</returns>
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

        if (app.Services.GetService<NoHttpEndpointsMarker>() is null)
        {
            app.UseFastEndpoints();
        }

        app.MapHealthChecks(options.HealthPath);
        app.MapHealthChecks(options.ReadyPath, new HealthCheckOptions
        {
            Predicate = registration => registration.Tags.Contains("ready", StringComparer.OrdinalIgnoreCase),
        });

        return app;
    }

    /// <summary>
    /// Runs the host through the JasperFx command-line pipeline instead of <c>app.Run()</c>.
    /// This is required for WolverineFx hosts so that operational commands — notably
    /// <c>codegen write</c>, which the container build invokes to pre-generate WolverineFx
    /// handlers before <c>dotnet publish</c> — are actually executed. When the process is
    /// started with no recognized command (the normal case) the pipeline simply starts the
    /// web host, so this is a drop-in replacement for <c>app.Run()</c>.
    /// </summary>
    /// <param name="app">The web application to run.</param>
    /// <param name="args">The process command-line arguments (forwarded to the command pipeline).</param>
    /// <returns>The process exit code; <c>0</c> for a normal host shutdown.</returns>
    public static Task<int> RunTeckServiceAsync(this WebApplication app, string[] args)
    {
        ArgumentNullException.ThrowIfNull(app);
        return app.RunJasperFxCommands(args);
    }

    /// <summary>
    /// Runs the host through the JasperFx command-line pipeline or applies pending EF Core migrations.
    /// </summary>
    /// <typeparam name="TDbContext">The write DbContext type that owns the host schema.</typeparam>
    /// <param name="app">The web application to run.</param>
    /// <param name="args">The process command-line arguments.</param>
    /// <returns>The process exit code; <c>0</c> after migrations complete or for a normal host shutdown.</returns>
    public static async Task<int> RunTeckServiceAsync<TDbContext>(this WebApplication app, string[] args)
        where TDbContext : DbContext
    {
        ArgumentNullException.ThrowIfNull(app);

        if (args is ["--migrate"])
        {
            await using AsyncServiceScope scope = app.Services.CreateAsyncScope();
            TDbContext dbContext = scope.ServiceProvider.GetRequiredService<TDbContext>();
            await dbContext.Database.MigrateAsync();
            return 0;
        }

        return await app.RunJasperFxCommands(args);
    }

    /// <summary>
    /// Marker singleton registered when a service assembly contains no HTTP endpoint declarations
    /// so that <see cref="UseTeckService"/> knows to skip <c>UseFastEndpoints()</c>.
    /// </summary>
    private sealed class NoHttpEndpointsMarker
    {
    }
}
