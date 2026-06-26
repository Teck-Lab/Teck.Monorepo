namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Base class for endpoints that require the caller to be authenticated and hold the "admin" role.
/// </summary>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TResponse">The response DTO type.</typeparam>
public abstract class AdminEndpoint<TRequest, TResponse> : AuthenticatedEndpoint<TRequest, TResponse>
    where TRequest : notnull
{
    /// <inheritdoc/>
    protected sealed override void ConfigureEndpoint()
    {
        Roles("admin");
        ConfigureAdminEndpoint();
    }

    /// <summary>
    /// Configures the admin-specific endpoint settings.
    /// </summary>
    protected abstract void ConfigureAdminEndpoint();
}
