using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace SharedKernel.Infrastructure.OpenApi;

/// <summary>
/// Filters operations by <see cref="OpenApiAudienceMetadata"/>.
/// </summary>
internal sealed class OpenApiAudienceOperationProcessor(string requiredAudience, bool includeUnannotated) : IOperationProcessor
{
    private readonly string requiredAudience = requiredAudience.Trim().ToLowerInvariant();
    private readonly bool includeUnannotated = includeUnannotated;

    public bool Process(OperationProcessorContext context)
    {
        if (string.IsNullOrWhiteSpace(requiredAudience))
        {
            return true;
        }

        if (context is not AspNetCoreOperationProcessorContext aspNetContext)
        {
            return includeUnannotated;
        }

        IList<object> endpointMetadata = aspNetContext.ApiDescription.ActionDescriptor.EndpointMetadata;
        OpenApiAudienceMetadata? audienceMetadata = endpointMetadata.OfType<OpenApiAudienceMetadata>().LastOrDefault();
        if (audienceMetadata is null)
        {
            return includeUnannotated;
        }

        return audienceMetadata
            .GetNormalizedAudiences()
            .Contains(requiredAudience, StringComparer.OrdinalIgnoreCase);
    }
}
