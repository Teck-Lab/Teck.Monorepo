using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;

namespace SharedKernel.Infrastructure;

/// <summary>
/// Shared hosting configuration for internal service transport.
/// </summary>
public static class GrpcHostingExtensions
{
    /// <summary>
    /// Enables protocol support required for internal gRPC traffic while preserving HTTP endpoints.
    /// </summary>
    /// <returns></returns>
    public static WebApplicationBuilder ConfigureInternalServiceTransport(this WebApplicationBuilder builder)
    {
        builder.WebHost.ConfigureKestrel(options =>
        {
            options.ConfigureEndpointDefaults(listenOptions =>
            {
                listenOptions.Protocols = HttpProtocols.Http1AndHttp2;
            });
        });

        return builder;
    }
}
