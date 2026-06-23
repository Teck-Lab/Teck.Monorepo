using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace SharedKernel.Infrastructure.Middlewares;

/// <summary>
/// Middleware that globally catches unhandled exceptions, logs them,
/// and returns a <see cref="ProblemDetails" /> response including
/// traceId and a list of error details.
/// </summary>
public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="GlobalExceptionHandlerMiddleware" /> class.
    /// </summary>
    /// <param name="next">The next middleware delegate in the pipeline.</param>
    /// <param name="logger">The logger to use for error logging.</param>
    public GlobalExceptionHandlerMiddleware(
        RequestDelegate next,
        ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    /// <summary>
    /// Processes an HTTP request and catches unhandled exceptions.
    /// </summary>
    /// <param name="context">The HTTP context.</param>
    /// <returns>A task that represents the completion of request processing.</returns>
    [RequiresDynamicCode("Calls HttpResponse.WriteAsJsonAsync which may require dynamic code at runtime.")]
    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception exception)
        {
            var traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;
            var user = context.User?.Identity?.Name ?? "anonymous";
            var path = context.Request.Path;

            _logger.LogError(
                exception,
                "Unhandled exception at {Path} [TraceId: {TraceId}, User: {User}]",
                path,
                traceId,
                user);

            var problem = new ProblemDetails
            {
                Status = 500,
                Title = "Internal Server Error",
                Type = "https://tools.ietf.org/html/rfc7231#section-6.6.1",
                Detail = "An unexpected error occurred. Please try again later.",
                Extensions =
                {
                    ["traceId"] = traceId,
                    ["errors"] = new[]
                    {
                        new { name = "server", reason = "An unexpected error occurred. Please contact support if the problem persists." }
                    }
                }
            };

            context.Response.StatusCode = 500;
            context.Response.ContentType = "application/problem+json";
            await context.Response.WriteAsync(JsonSerializer.Serialize(problem));
        }
    }
}
