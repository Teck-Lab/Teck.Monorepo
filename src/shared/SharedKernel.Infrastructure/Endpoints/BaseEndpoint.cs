using FastEndpoints;
using Microsoft.AspNetCore.Authentication.JwtBearer;

namespace SharedKernel.Infrastructure.Endpoints;

public abstract class AuthenticatedEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    public sealed override void Configure()
    {
        AuthSchemes(JwtBearerDefaults.AuthenticationScheme);
        ConfigureEndpoint();
    }

    protected abstract void ConfigureEndpoint();
}

public abstract class AdminEndpoint<TRequest, TResponse> : AuthenticatedEndpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected sealed override void ConfigureEndpoint()
    {
        Roles("admin");
        ConfigureAdminEndpoint();
    }

    protected abstract void ConfigureAdminEndpoint();
}
