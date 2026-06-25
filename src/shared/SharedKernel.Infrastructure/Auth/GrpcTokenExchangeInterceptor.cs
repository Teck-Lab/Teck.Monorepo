using Grpc.Core;
using Grpc.Core.Interceptors;
using Microsoft.AspNetCore.Http;

namespace SharedKernel.Infrastructure.Auth;

/// <summary>
/// Enriches outbound gRPC calls with exchanged access token and tenant headers.
/// </summary>
public sealed class GrpcTokenExchangeInterceptor : Interceptor
{
    private const string AuthorizationHeader = "authorization";
    private const string TenantIdHeader = "x-tenantid";
    private const string TenantDbStrategyHeader = "x-tenant-dbstrategy";

    private readonly IHttpContextAccessor _httpContextAccessor;
    private readonly IOutboundSecurityContextFactory _securityContextFactory;
    private readonly string _audience;

    /// <summary>
    /// Initializes a new instance of the <see cref="GrpcTokenExchangeInterceptor"/> class.
    /// </summary>
    /// <param name="httpContextAccessor">Accessor for the current HTTP context.</param>
    /// <param name="securityContextFactory">Factory that creates outbound security context data.</param>
    /// <param name="audience">Target audience for token exchange.</param>
    public GrpcTokenExchangeInterceptor(
        IHttpContextAccessor httpContextAccessor,
        IOutboundSecurityContextFactory securityContextFactory,
        string audience)
    {
        _httpContextAccessor = httpContextAccessor;
        _securityContextFactory = securityContextFactory;
        _audience = audience;
    }

    /// <summary>
    /// Intercepts unary calls and applies outbound authentication and tenant headers.
    /// </summary>
    /// <typeparam name="TRequest">The request message type.</typeparam>
    /// <typeparam name="TResponse">The response message type.</typeparam>
    /// <param name="request">The outgoing request payload.</param>
    /// <param name="context">The gRPC client interceptor context.</param>
    /// <param name="continuation">The continuation delegate for invoking the next interceptor/call.</param>
    /// <returns>An asynchronous unary call wrapper for the response.</returns>
    public override AsyncUnaryCall<TResponse> AsyncUnaryCall<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
    {
        return ContinueWithHeadersAsync(request, context, continuation);
    }

    private AsyncUnaryCall<TResponse> ContinueWithHeadersAsync<TRequest, TResponse>(
        TRequest request,
        ClientInterceptorContext<TRequest, TResponse> context,
        AsyncUnaryCallContinuation<TRequest, TResponse> continuation)
        where TRequest : class
        where TResponse : class
    {
        OutboundSecurityContext outbound = _securityContextFactory
            .CreateAsync(_httpContextAccessor.HttpContext, _audience, CancellationToken.None)
            .ConfigureAwait(false)
            .GetAwaiter()
            .GetResult();

        Metadata headers = [];
        if (context.Options.Headers is not null)
        {
            foreach (Metadata.Entry entry in context.Options.Headers)
            {
                headers.Add(entry);
            }
        }

        ApplyHeaders(headers, outbound);

        CallOptions callOptions = context.Options.WithHeaders(headers);
        ClientInterceptorContext<TRequest, TResponse> nextContext =
            new(context.Method, context.Host, callOptions);

        AsyncUnaryCall<TResponse> call = continuation(request, nextContext);

        return new AsyncUnaryCall<TResponse>(
            call.ResponseAsync,
            call.ResponseHeadersAsync,
            call.GetStatus,
            call.GetTrailers,
            call.Dispose);
    }

    private static void ApplyHeaders(Metadata headers, OutboundSecurityContext outbound)
    {
        if (!string.IsNullOrWhiteSpace(outbound.AccessToken))
        {
            RemoveAll(headers, AuthorizationHeader);
            headers.Add(AuthorizationHeader, $"Bearer {outbound.AccessToken}");
        }

        if (!string.IsNullOrWhiteSpace(outbound.TenantId))
        {
            RemoveAll(headers, TenantIdHeader);
            headers.Add(TenantIdHeader, outbound.TenantId);
        }

        if (!string.IsNullOrWhiteSpace(outbound.TenantDbStrategy))
        {
            RemoveAll(headers, TenantDbStrategyHeader);
            headers.Add(TenantDbStrategyHeader, outbound.TenantDbStrategy);
        }
    }

    private static void RemoveAll(Metadata headers, string key)
    {
        for (int index = headers.Count - 1; index >= 0; index--)
        {
            if (string.Equals(headers[index].Key, key, StringComparison.OrdinalIgnoreCase))
            {
                headers.RemoveAt(index);
            }
        }
    }
}
