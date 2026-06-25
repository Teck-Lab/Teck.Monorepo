using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Represents the supported API versions.
/// </summary>
public enum ApiVersion
{
    /// <summary>
    /// API version 1.
    /// </summary>
    V1 = 1,

    /// <summary>
    /// API version 2.
    /// </summary>
    V2 = 2,
}

/// <summary>
/// Base class for endpoints that resolve the requested API version from the incoming request.
/// </summary>
/// <typeparam name="TRequest">The request DTO type.</typeparam>
/// <typeparam name="TResponse">The response DTO type.</typeparam>
public abstract class VersionedEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    /// <summary>
    /// Gets the API version requested by the caller.
    /// </summary>
    protected ApiVersion RequestedVersion { get; private set; } = ApiVersion.V1;

    /// <inheritdoc/>
    public sealed override void Configure()
    {
        ConfigureEndpoint();
    }

    /// <inheritdoc/>
    public sealed override Task OnBeforeHandleAsync(TRequest req, CancellationToken ct)
    {
        string? requestedVersion = HttpContext.Request.Headers.TryGetValue("api-version", out var headerValues) && !string.IsNullOrWhiteSpace(headerValues.FirstOrDefault())
            ? headerValues.FirstOrDefault()
            : HttpContext.Request.Query.TryGetValue("v", out var queryValues) && !string.IsNullOrWhiteSpace(queryValues.FirstOrDefault())
                ? queryValues.FirstOrDefault()
                : "1";

        if (!int.TryParse(requestedVersion, out int versionValue) || !Enum.IsDefined(typeof(ApiVersion), versionValue))
        {
            throw new BadHttpRequestException($"Unsupported API version '{requestedVersion}'.");
        }

        RequestedVersion = (ApiVersion)versionValue;

        return Task.CompletedTask;
    }

    /// <summary>
    /// Configures the endpoint-specific settings.
    /// </summary>
    protected abstract void ConfigureEndpoint();

    /// <summary>
    /// Sets the API version for this endpoint.
    /// </summary>
    /// <param name="version">The API version to apply.</param>
    protected void Version(ApiVersion version) => base.Version((int)version);
}
