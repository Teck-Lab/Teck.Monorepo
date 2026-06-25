using FastEndpoints;
using Microsoft.AspNetCore.Http;

namespace SharedKernel.Infrastructure.Endpoints;

public enum ApiVersion
{
    V1 = 1,
    V2 = 2,
}

public abstract class VersionedEndpoint<TRequest, TResponse> : Endpoint<TRequest, TResponse>
    where TRequest : notnull
{
    protected ApiVersion RequestedVersion { get; private set; } = ApiVersion.V1;

    /// <inheritdoc/>
    public sealed override void Configure()
    {
        ConfigureEndpoint();
    }

    protected abstract void ConfigureEndpoint();

    protected void Version(ApiVersion version) => base.Version((int)version);

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
}
