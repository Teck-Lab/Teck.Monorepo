using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Base class for endpoints that require the caller to be authenticated via JWT bearer authentication.
/// </summary>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TResponse">The response DTO type.</typeparam>
public abstract class AuthenticatedEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc/>
    public sealed override void Configure()
    {
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        ConfigureEndpoint();
    }

    /// <summary>
    /// Configures the endpoint-specific settings.
    /// </summary>
    protected abstract void ConfigureEndpoint();
}
