using System.Diagnostics;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;

namespace Gateway.Public.Edge;

/// <summary>Writes an <see cref="EdgeProblem"/> as RFC-7807 <c>application/problem+json</c>.</summary>
public static class EdgeProblemWriter
{
    /// <summary>Writes the problem to the HTTP response; no-op if the response has already started.</summary>
    /// <param name="context">The HTTP context whose response is written.</param>
    /// <param name="problem">The problem to serialize.</param>
    /// <returns>A <see cref="Task"/> that completes when the response body is flushed.</returns>
    public static async Task WriteAsync(HttpContext context, EdgeProblem problem)
    {
        if (context.Response.HasStarted)
        {
            return;
        }

        string traceId = Activity.Current?.TraceId.ToString() ?? context.TraceIdentifier;

        var details = new ProblemDetails
        {
            Status = problem.StatusCode,
            Title = problem.Title,
            Detail = problem.Detail,
            Type = problem.StatusCode is 401 or 403
                ? "https://tools.ietf.org/html/rfc7235"
                : "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Instance = context.Request.Path,
        };
        details.Extensions["traceId"] = traceId;
        details.Extensions["errors"] = new[] { new { name = problem.ErrorCode, reason = problem.Detail } };

        context.Response.StatusCode = problem.StatusCode;
        context.Response.ContentType = "application/problem+json";
        await context.Response.WriteAsync(JsonSerializer.Serialize(details));
    }
}
