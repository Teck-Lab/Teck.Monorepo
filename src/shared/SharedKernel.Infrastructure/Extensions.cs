using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Infrastructure.Middlewares;
using SharedKernel.Infrastructure.Options;

namespace SharedKernel.Infrastructure;

/// <summary>
/// The extensions.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Allow all origins.
    /// </summary>
    public const string AllowAllOrigins = "AllowAll";

    /// <summary>
    /// Add the infrastructure.
    /// </summary>
    /// <param name="builder">The builder.</param>
    /// <param name="appOptions"></param>
    public static void AddBaseInfrastructure(
        this WebApplicationBuilder builder,
        AppOptions appOptions)
    {
        _ = appOptions;

        // 1. Core services
        builder.Services.Configure<JsonOptions>(options =>
        {
            options.SerializerOptions.ReferenceHandler = ReferenceHandler.IgnoreCycles;
        });

        builder.Services.AddHttpContextAccessor();

        builder.Services.AddRouting(options => options.LowercaseUrls = true);

        // 2. Authentication/Authorization baseline
        builder.Services.AddAuthentication();
        builder.Services.AddAuthorization();

        // 3. CORS
        builder.Services.AddCors(options =>
        {
            options.AddPolicy("AllowAllOrigins", policy =>
                policy.AllowAnyOrigin().AllowAnyMethod().AllowAnyHeader());
        });

        // 4. Forwarded headers
        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor
                                     | ForwardedHeaders.XForwardedProto
                                     | ForwardedHeaders.XForwardedHost;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
        });

        // NOTE: HTTP logging removed (was temporarily added for debugging forwarded headers).
    }

    /// <summary>
    /// Use the infrastructure.
    /// </summary>
    /// <param name="app">The app.</param>
    public static void UseBaseInfrastructure(
        this WebApplication app)
    {
        // Add global exception handler middleware here
        app.UseMiddleware<GlobalExceptionHandlerMiddleware>();

        // Preserve Order
        app.UseCors(AllowAllOrigins);
        app.UseForwardedHeaders();

        app.Use((context, next) =>
        {
            if (context.Request.Headers.TryGetValue("X-Forwarded-Prefix", out var prefix))
            {
                context.Request.PathBase = new PathString(prefix);
            }

            return next();
        });

        app.UseAuthentication();
        app.UseAuthorization();
    }
}
