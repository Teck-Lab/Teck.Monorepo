using Scalar.AspNetCore;

namespace Gateway.Edge.Contracts;

/// <summary>Serves the curated, versioned audience-specific gateway contract and Scalar UI.</summary>
public static class GatewayOpenApiEndpoints
{
    /// <summary>Maps the v1 contract and its Scalar UI with identical audience visibility.</summary>
    /// <param name="app">The gateway application.</param>
    /// <param name="audience">The gateway audience, customer or admin.</param>
    public static void MapGatewayOpenApi(this WebApplication app, string audience)
    {
        if (audience is not ("customer" or "admin"))
        {
            throw new ArgumentOutOfRangeException(nameof(audience));
        }

        var assembly = typeof(GatewayOpenApiEndpoints).Assembly;
        string resource = $"Gateway.Edge.Contracts.gateway-{audience}-v1.openapi.json";
        using Stream document = assembly.GetManifestResourceStream(resource)
            ?? throw new InvalidOperationException($"Missing embedded gateway OpenAPI resource '{resource}'.");
        using var reader = new StreamReader(document);
        string json = reader.ReadToEnd();

        RouteHandlerBuilder openApi = app.MapGet("/openapi/v1.json", () => Results.Text(json, "application/json"))
            .ExcludeFromDescription();
        var scalar = app.MapScalarApiReference("/scalar/v1", options =>
        {
            options.WithOpenApiRoutePattern("/openapi/v1.json");
            options.AddDocument("v1");
        });

        if (audience == "admin")
        {
            openApi.RequireAuthorization("developer");
            scalar.RequireAuthorization("developer");
        }
    }
}
