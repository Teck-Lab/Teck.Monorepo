using FastEndpoints;
using Keycloak.AuthServices.Authorization;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using SharedKernel.Infrastructure.OpenApi;

namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Base class for service endpoints. Each endpoint declares its <see cref="Permission"/>; the base
/// wires Keycloak protected-resource authorization (or anonymous access) plus the OpenAPI audience
/// document, so authorization can never be silently omitted.
/// </summary>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TResponse">The response DTO type.</typeparam>
public abstract class AuthenticatedEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>Gets the access policy for this endpoint.</summary>
    protected abstract EndpointPermission Permission { get; }

    /// <inheritdoc/>
    public sealed override void Configure()
    {
        ConfigureEndpoint();

        EndpointPermission permission = Permission;

        Metadata(new OpenApiAudienceMetadata(permission.Audience));

        Options(builder =>
        {
            if (!permission.IsAnonymous)
            {
                builder.RequireProtectedResource(permission.Resource, permission.Scope);
            }
        });

        if (permission.IsAnonymous)
        {
            AllowAnonymous();
        }
        else
        {
            AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        }
    }

    /// <summary>Configures route, version, and summary for this endpoint.</summary>
    protected abstract void ConfigureEndpoint();
}
