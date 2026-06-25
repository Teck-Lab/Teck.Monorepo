using System.Reflection;
using System.Text.Json.Serialization;
using FastEndpoints;
using FluentValidation;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// FastEndpoints infrastructure setup extensions.
/// </summary>
public static class Extensions
{
    /// <summary>
    /// Registers FastEndpoints and wires validator discovery with idempotency middleware support.
    /// </summary>
    /// <param name="services">The service collection.</param>
    /// <param name="assemblies">Optional assemblies to scan for endpoint and validator types.</param>
    /// <returns>The updated service collection instance.</returns>
    public static IServiceCollection AddFastEndpointsInfrastructure(
        this IServiceCollection services,
        params Assembly[] assemblies)
    {
        Assembly[] resolvedAssemblies = assemblies
            .Where(static assembly => assembly is not null)
            .Distinct()
            .ToArray();

        services
            .AddFastEndpoints(options =>
            {
                if (resolvedAssemblies.Length > 0)
                {
                    options.Assemblies = resolvedAssemblies;
                }
            })
            .AddIdempotency();

        foreach (Assembly assembly in resolvedAssemblies)
        {
            services.AddValidatorsFromAssembly(
                assembly,
                includeInternalTypes: true,
                filter: filter =>
                {
                    Type? baseType = filter.ValidatorType.BaseType;
                    return baseType?.IsGenericType != true
                        || baseType.GetGenericTypeDefinition() != typeof(FastEndpoints.Validator<>);
                });
        }

        return services;
    }

    /// <summary>
    /// Enables the FastEndpoints runtime pipeline using the default route prefix behavior.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <returns>The same application builder for fluent chaining.</returns>
    public static IApplicationBuilder UseFastEndpointsInfrastructure(this IApplicationBuilder app)
    {
        return UseFastEndpointsInfrastructure(app, null);
    }

    /// <summary>
    /// Enables the FastEndpoints runtime pipeline and applies an explicit route prefix when provided.
    /// </summary>
    /// <param name="app">The application builder.</param>
    /// <param name="routePrefix">Optional service route prefix (for example: customer, catalog).</param>
    /// <returns>The same application builder for fluent chaining.</returns>
    public static IApplicationBuilder UseFastEndpointsInfrastructure(
        this IApplicationBuilder app,
        string? routePrefix)
    {
        WebApplication webApplication = (WebApplication)app;

        webApplication.UseOutputCache();

        webApplication.UseFastEndpoints(config =>
        {
            config.Errors.ResponseBuilder = (failures, context, statusCode) =>
            {
                var problemDetails = new Microsoft.AspNetCore.Mvc.ValidationProblemDetails(
                    failures.GroupBy(failure => failure.PropertyName)
                        .ToDictionary(
                            keySelector: group => group.Key,
                            elementSelector: group => group.Select(failure => failure.ErrorMessage).ToArray()))
                {
                    Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                    Title = "One or more validation errors occurred.",
                    Status = statusCode,
                    Instance = context.Request.Path,
                };

                problemDetails.Extensions["traceId"] = context.TraceIdentifier;

                return problemDetails;
            };

            config.Serializer.Options.ReferenceHandler = ReferenceHandler.IgnoreCycles;
            config.Endpoints.RoutePrefix = string.IsNullOrWhiteSpace(routePrefix)
                ? null
                : routePrefix.Trim('/');
            config.Versioning.Prefix = "v";
            config.Versioning.PrependToRoute = true;
            config.Versioning.DefaultVersion = 1;
        });

        return webApplication;
    }
}
