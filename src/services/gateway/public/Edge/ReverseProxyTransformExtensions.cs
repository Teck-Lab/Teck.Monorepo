using Yarp.ReverseProxy.Transforms;

namespace Gateway.Public.Edge;

/// <summary>Forwards trusted edge headers and the exchanged bearer to the upstream.</summary>
public static class ReverseProxyTransformExtensions
{
    /// <summary>Adds the edge request transforms that forward tenant headers and the exchanged bearer token.</summary>
    /// <param name="builder">The reverse proxy builder.</param>
    /// <param name="tenantOptions">The edge tenant options.</param>
    /// <returns>The same builder.</returns>
    public static IReverseProxyBuilder AddEdgeGatewayTransforms(
        this IReverseProxyBuilder builder,
        EdgeTenantOptions tenantOptions) =>
        builder.AddTransforms(ctx => ctx.AddRequestTransform(transform =>
        {
            HttpContext http = transform.HttpContext;
            ForwardHeader(transform, http, tenantOptions.TenantIdHeaderName);
            ForwardHeader(transform, http, EdgeHeaders.TenantDbStrategy);

            if (http.Items.TryGetValue(EdgeHeaders.ExchangedTokenItemKey, out object? token) &&
                token is string exchanged &&
                !string.IsNullOrWhiteSpace(exchanged))
            {
                transform.ProxyRequest.Headers.Authorization = new("Bearer", exchanged);
            }

            return ValueTask.CompletedTask;
        }));

    private static void ForwardHeader(RequestTransformContext transform, HttpContext http, string headerName)
    {
        if (http.Request.Headers.TryGetValue(headerName, out var values) &&
            !string.IsNullOrWhiteSpace(values.ToString()))
        {
            transform.ProxyRequest.Headers.Remove(headerName);
            transform.ProxyRequest.Headers.TryAddWithoutValidation(headerName, values.ToString());
        }
    }
}
