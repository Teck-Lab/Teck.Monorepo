using System.Diagnostics;
using System.Text.Json;
using Keycloak.AuthServices.Authentication;
using Keycloak.AuthServices.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Infrastructure.Auth
{
    /// <summary>
    /// Provides authentication and authorization setup using Keycloak.
    /// Can be used in both web APIs and proxy gateways (e.g., YARP).
    /// </summary>
    public static class Extensions
    {
        /// <summary>
        /// Configures Keycloak authentication and authorization.
        /// This extension does not rely on any web framework (e.g., FastEndpoints), making it safe for YARP.
        /// </summary>
        /// <param name="services">The application's service collection.</param>
        /// <param name="config">The application configuration (used to bind Keycloak options).</param>
        /// <param name="env">The hosting environment (used to determine HTTPS requirements).</param>
        /// <param name="keycloakOptions">Bound Keycloak options, e.g., realm, URL, resource name.</param>
        /// <returns>The updated <see cref="IServiceCollection"/>.</returns>
        public static IServiceCollection AddKeycloak(
            this IServiceCollection services,
            IConfiguration config,
            IHostEnvironment env,
            KeycloakAuthenticationOptions keycloakOptions)
        {
            bool isProduction = env.IsProduction();

            // Configure JWT Bearer authentication from Keycloak
            services.AddKeycloakWebApiAuthentication(config, options =>
            {
                options.IncludeErrorDetails = true;
                options.Authority = keycloakOptions.KeycloakUrlRealm;
                options.Audience = keycloakOptions.Resource;
                options.RequireHttpsMetadata = isProduction;
                options.SaveToken = true;

                // Customize token validation
                options.TokenValidationParameters = new()
                {
                    RequireAudience = true,
                    ValidateAudience = true,
                };

                // Custom response for unauthorized and forbidden access
                options.Events = new JwtBearerEvents
                {
                    OnChallenge = async context =>
                    {
                        context.HandleResponse();

                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("JwtBearerEvents");

                        var traceId = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
                        var userId = context.HttpContext.User?.Identity?.Name ?? "anonymous";

                        var details = new[]
                        {
                            new
                            {
                                name = "authorization",
                                reason = context.ErrorDescription ?? "Authentication is required to access this resource."
                            }
                        };

                        var problem = new ProblemDetails
                        {
                            Status = 401,
                            Title = "Unauthorized",
                            Type = "https://tools.ietf.org/html/rfc7235",
                            Detail = "Authentication failed or was missing.",
                            Extensions =
                            {
                                ["traceId"] = traceId,
                                ["errors"] = details
                            }
                        };

                        logger.LogWarning(
                            "401 Unauthorized - Path: {Path}, User: {User}, TraceId: {TraceId}, Error: {Error}, Description: {Description}",
                            context.HttpContext.Request.Path,
                            userId,
                            traceId,
                            context.Error ?? "N/A",
                            context.ErrorDescription ?? "N/A");

                        context.Response.StatusCode = problem.Status ?? 401;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
                    },

                    OnForbidden = async context =>
                    {
                        var logger = context.HttpContext.RequestServices
                            .GetRequiredService<ILoggerFactory>()
                            .CreateLogger("JwtBearerEvents");

                        var traceId = Activity.Current?.TraceId.ToString() ?? context.HttpContext.TraceIdentifier;
                        var userId = context.HttpContext.User?.Identity?.Name ?? "anonymous";

                        var details = new[]
                        {
                            new
                            {
                                name = "authorization",
                                reason = "You do not have permission to access this resource."
                            }
                        };

                        var problem = new ProblemDetails
                        {
                            Status = 403,
                            Title = "Forbidden",
                            Type = "https://tools.ietf.org/html/rfc7235",
                            Detail = "Access denied due to insufficient permissions.",
                            Extensions =
                            {
                                ["traceId"] = traceId,
                                ["errors"] = details
                            }
                        };

                        logger.LogWarning(
                            "403 Forbidden - Path: {Path}, User: {User}, TraceId: {TraceId}",
                            context.HttpContext.Request.Path,
                            userId,
                            traceId);

                        context.Response.StatusCode = problem.Status ?? 403;
                        context.Response.ContentType = "application/problem+json";
                        await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
                    }
                };
            });

            // Register authorization using Keycloak's authorization services
            services.AddAuthorization()
                    .AddKeycloakAuthorization()
                    .AddAuthorizationServer(config);

            // Add health check for Keycloak's OpenID Connect server.
            // Prefer AuthServerUrl+Realm when available, otherwise fall back to Authority/KeycloakUrlRealm.
            var openIdBaseUrl = keycloakOptions.AuthServerUrl;
            if (string.IsNullOrWhiteSpace(openIdBaseUrl))
            {
                if (!string.IsNullOrWhiteSpace(keycloakOptions.KeycloakUrlRealm))
                {
                    openIdBaseUrl = keycloakOptions.KeycloakUrlRealm;
                }
                else
                {
                    openIdBaseUrl = config["Keycloak:Authority"];
                }
            }

            if (!string.IsNullOrWhiteSpace(openIdBaseUrl))
            {
                string healthEndpoint;
                if (!string.IsNullOrWhiteSpace(keycloakOptions.Realm) &&
                    !openIdBaseUrl.Contains("/realms/", StringComparison.OrdinalIgnoreCase))
                {
                    healthEndpoint = $"{openIdBaseUrl.TrimEnd('/')}/realms/{keycloakOptions.Realm.Trim('/')}/";
                }
                else
                {
                    healthEndpoint = $"{openIdBaseUrl.TrimEnd('/')}/";
                }

                if (Uri.TryCreate(healthEndpoint, UriKind.Absolute, out var openIdUri))
                {
                    services.AddHealthChecks().AddOpenIdConnectServer(
                        openIdUri,
                        name: "keycloak-openid",
                        tags: ["identity", "keycloak", "openid", "ready"],
                        failureStatus: HealthStatus.Degraded);
                }
            }

            return services;
        }
    }
}
