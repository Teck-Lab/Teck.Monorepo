using System.Reflection;
using System.Runtime.InteropServices;
using FastEndpoints.Swagger;
using Keycloak.AuthServices.Authentication;
using Keycloak.AuthServices.Common;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using NSwag;
using NSwag.Generation.Processors.Security;
using Scalar.AspNetCore;
using SharedKernel.Infrastructure.Options;

namespace SharedKernel.Infrastructure.OpenApi;

/// <summary>
/// OpenAPI setup helpers.
/// </summary>
public static class OpenApiExtensions
{
    /// <summary>
    /// Registers OpenAPI documents for configured versions.
    /// </summary>
    public static void AddOpenApiInfrastructure(this WebApplicationBuilder builder, AppOptions appOptions)
    {
        var keycloakOptions = builder.Configuration.GetKeycloakOptions<KeycloakAuthenticationOptions>();
        string applicationVersion = ResolveApplicationVersion();
        List<int> apiVersions = appOptions.Versions
            .Where(version => version > 0)
            .Distinct()
            .OrderBy(version => version)
            .ToList();

        if (apiVersions.Count == 0)
        {
            apiVersions.Add(1);
        }

        List<Action<DocumentOptions>> documentOptions = new List<Action<DocumentOptions>>();

        foreach (var apiVersion in apiVersions)
        {
            Action<DocumentOptions> allDocument = new(options =>
            {
                options.EnableJWTBearerAuth = false;
                options.MaxEndpointVersion = apiVersion;
                options.DocumentSettings = settings =>
                {
                    settings.Version = applicationVersion;
                    settings.Title = $"{appOptions.Name} API";
                    settings.DocumentName = $"v{apiVersion}";
                    settings.Description = appOptions.Description;

                    if (keycloakOptions is not null)
                    {
                        settings.AddSecurity(
                            "oAuth2",
                            BuildOAuthScheme(
                                keycloakOptions.KeycloakTokenEndpoint,
                                keycloakOptions.KeycloakUrlRealm + "protocol/openid-connect/auth",
                                keycloakOptions.KeycloakTokenEndpoint));
                    }

                    settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor("oAuth2"));
                };
            });

            documentOptions.Add(allDocument);

            Action<DocumentOptions> publicDocument = new(options =>
            {
                options.EnableJWTBearerAuth = false;
                options.MaxEndpointVersion = apiVersion;
                options.DocumentSettings = settings =>
                {
                    settings.Version = applicationVersion;
                    settings.Title = $"{appOptions.Name} Public API";
                    settings.DocumentName = $"v{apiVersion}-public";
                    settings.Description = appOptions.Description;

                    if (keycloakOptions is not null)
                    {
                        settings.AddSecurity(
                            "oAuth2",
                            BuildOAuthScheme(
                                keycloakOptions.KeycloakTokenEndpoint,
                                keycloakOptions.KeycloakUrlRealm + "protocol/openid-connect/auth",
                                keycloakOptions.KeycloakTokenEndpoint));
                    }

                    settings.OperationProcessors.Add(new OpenApiAudienceOperationProcessor("public", includeUnannotated: false));
                    settings.OperationProcessors.Add(new AspNetCoreOperationSecurityScopeProcessor("oAuth2"));
                };
            });

            documentOptions.Add(publicDocument);
        }

        foreach (ref Action<DocumentOptions> option in CollectionsMarshal.AsSpan(documentOptions))
        {
            builder.Services.SwaggerDocument(option);
        }
    }

    /// <summary>
    /// Maps OpenAPI JSON route and Scalar UI.
    /// </summary>
    public static void UseOpenApiInfrastructure(this WebApplication app, AppOptions appOptions)
    {
        List<int> apiVersions = appOptions.Versions
            .Where(version => version > 0)
            .Distinct()
            .OrderBy(version => version)
            .ToList();

        if (apiVersions.Count == 0)
        {
            apiVersions.Add(1);
        }

        app.UseSwaggerGen(document =>
        {
            document.Path = "/openapi/{documentName}/openapi.json";
        });

        app.MapScalarApiReference("docs", options =>
        {
            options.WithOpenApiRoutePattern("/openapi/{documentName}/openapi.json");
            foreach (int apiVersion in apiVersions)
            {
                options.AddDocument($"v{apiVersion}");
            }

            options
                .AddPreferredSecuritySchemes("oAuth2")
                .AddAuthorizationCodeFlow("oAuth2", flow =>
                {
                    flow.ClientId = app.Configuration["Keycloak:ScalarClientId"] ?? "scalar-ui";
                    flow.Pkce = Pkce.Sha256;
                });
        });
    }

    private static OpenApiSecurityScheme BuildOAuthScheme(string tokenUrl, string authorizationUrl, string refreshUrl)
    {
        return new OpenApiSecurityScheme
        {
            In = OpenApiSecurityApiKeyLocation.Header,
            Type = OpenApiSecuritySchemeType.OAuth2,
            Flows = new OpenApiOAuthFlows
            {
                AuthorizationCode = new OpenApiOAuthFlow
                {
                    AuthorizationUrl = authorizationUrl,
                    TokenUrl = tokenUrl,
                    RefreshUrl = refreshUrl,
                    Scopes = new Dictionary<string, string>
                    {
                        ["organization"] = "Org",
                    },
                },
            },
        };
    }

    private static string ResolveApplicationVersion()
    {
        Assembly? entryAssembly = Assembly.GetEntryAssembly();
        if (entryAssembly is null)
        {
            return "1.0.0";
        }

        string? informationalVersion = entryAssembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;

        if (!string.IsNullOrWhiteSpace(informationalVersion))
        {
            return informationalVersion;
        }

        Version? assemblyVersion = entryAssembly.GetName().Version;
        if (assemblyVersion is null)
        {
            return "1.0.0";
        }

        int build = assemblyVersion.Build >= 0 ? assemblyVersion.Build : 0;
        return $"{assemblyVersion.Major}.{assemblyVersion.Minor}.{build}";
    }
}
