using NSwag.Generation.AspNetCore;
using NSwag.Generation.Processors;
using NSwag.Generation.Processors.Contexts;

namespace SharedKernel.Infrastructure.OpenApi;

/// <summary>
/// Adds Keycloak protected-resource requirements from FastEndpoints endpoint metadata to OpenAPI operations.
/// </summary>
internal sealed class OpenApiProtectedResourceOperationProcessor : IOperationProcessor
{
    public bool Process(OperationProcessorContext context)
    {
        if (context is not AspNetCoreOperationProcessorContext aspNetContext)
        {
            return true;
        }

        IList<object> endpointMetadata = aspNetContext.ApiDescription.ActionDescriptor.EndpointMetadata;
        OpenApiProtectedResourceMetadata? permission = endpointMetadata.OfType<OpenApiProtectedResourceMetadata>()
            .LastOrDefault();
        if (permission is not null)
        {
            context.OperationDescription.Operation.ExtensionData ??= new Dictionary<string, object?>();
            context.OperationDescription.Operation.ExtensionData["x-protected-resource"] = new
            {
                resource = permission.Resource,
                scope = permission.Scope,
            };
        }

        return true;
    }
}
