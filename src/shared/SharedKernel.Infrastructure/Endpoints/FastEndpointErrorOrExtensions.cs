using System.Diagnostics.CodeAnalysis;
using ErrorOr;
using FastEndpoints;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Provides helpers to map ErrorOr results to FastEndpoints responses.
/// </summary>
public static class FastEndpointErrorOrExtensions
{
    /// <summary>
    /// Sends either a successful response or problem details for an ErrorOr result.
    /// </summary>
    /// <typeparam name="TResponse">The type of the successful response value.</typeparam>
    /// <param name="endpoint">The endpoint whose HTTP context is used to write the response.</param>
    /// <param name="result">The ErrorOr result containing either the response value or the errors to report.</param>
    /// <param name="successStatusCode">The HTTP status code to set when the result is successful.</param>
    /// <param name="cancellation">The token used to cancel writing the response.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    [RequiresDynamicCode("Calls HttpResponse.WriteAsJsonAsync which may require dynamic code at runtime.")]
    public static async Task SendAsync<TResponse>(
        this EndpointWithoutRequest<TResponse> endpoint,
        ErrorOr<TResponse> result,
        int successStatusCode = StatusCodes.Status200OK,
        CancellationToken cancellation = default)
        where TResponse : notnull
    {
        if (!result.IsError)
        {
            endpoint.HttpContext.Response.StatusCode = successStatusCode;
            await endpoint.HttpContext.Response.WriteAsJsonAsync(result.Value, cancellationToken: cancellation).ConfigureAwait(false);
            return;
        }

        await SendProblemAsync(endpoint.HttpContext, result.Errors, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends either a successful response or problem details for an ErrorOr result.
    /// </summary>
    /// <typeparam name="TRequest">The type of the endpoint's request model.</typeparam>
    /// <typeparam name="TResponse">The type of the successful response value.</typeparam>
    /// <param name="endpoint">The endpoint whose HTTP context is used to write the response.</param>
    /// <param name="result">The ErrorOr result containing either the response value or the errors to report.</param>
    /// <param name="successStatusCode">The HTTP status code to set when the result is successful.</param>
    /// <param name="cancellation">The token used to cancel writing the response.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    [RequiresDynamicCode("Calls HttpResponse.WriteAsJsonAsync which may require dynamic code at runtime.")]
    public static async Task SendAsync<TRequest, TResponse>(
        this Endpoint<TRequest, TResponse> endpoint,
        ErrorOr<TResponse> result,
        int successStatusCode = StatusCodes.Status200OK,
        CancellationToken cancellation = default)
        where TRequest : notnull
    {
        if (!result.IsError)
        {
            endpoint.HttpContext.Response.StatusCode = successStatusCode;
            await endpoint.HttpContext.Response.WriteAsJsonAsync(result.Value, cancellationToken: cancellation).ConfigureAwait(false);
            return;
        }

        await SendProblemAsync(endpoint.HttpContext, result.Errors, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends either a no-content success or problem details for an ErrorOr deleted result.
    /// </summary>
    /// <typeparam name="TRequest">The type of the endpoint's request model.</typeparam>
    /// <typeparam name="TResponse">The type of the endpoint's response model.</typeparam>
    /// <param name="endpoint">The endpoint whose HTTP context is used to write the response.</param>
    /// <param name="result">The ErrorOr deleted result indicating success or the errors to report.</param>
    /// <param name="cancellation">The token used to cancel writing the response.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task SendNoContentAsync<TRequest, TResponse>(
        this Endpoint<TRequest, TResponse> endpoint,
        ErrorOr<Deleted> result,
        CancellationToken cancellation = default)
        where TRequest : notnull
    {
        if (!result.IsError)
        {
            endpoint.HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        await SendProblemAsync(endpoint.HttpContext, result.Errors, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends either a no-content success or problem details for an ErrorOr result.
    /// </summary>
    /// <typeparam name="TRequest">The type of the endpoint's request model.</typeparam>
    /// <typeparam name="TResponse">The type of the endpoint's response model.</typeparam>
    /// <typeparam name="TValue">The type of the value carried by the ErrorOr result.</typeparam>
    /// <param name="endpoint">The endpoint whose HTTP context is used to write the response.</param>
    /// <param name="result">The ErrorOr result indicating success or the errors to report.</param>
    /// <param name="cancellation">The token used to cancel writing the response.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task SendNoContentAsync<TRequest, TResponse, TValue>(
        this Endpoint<TRequest, TResponse> endpoint,
        ErrorOr<TValue> result,
        CancellationToken cancellation = default)
        where TRequest : notnull
    {
        if (!result.IsError)
        {
            endpoint.HttpContext.Response.StatusCode = StatusCodes.Status204NoContent;
            return;
        }

        await SendProblemAsync(endpoint.HttpContext, result.Errors, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends either a 200 OK (empty body) or problem details for an ErrorOr Success result.
    /// </summary>
    /// <typeparam name="TRequest">The type of the endpoint's request model.</typeparam>
    /// <typeparam name="TResponse">The type of the endpoint's response model.</typeparam>
    /// <param name="endpoint">The endpoint whose HTTP context is used to write the response.</param>
    /// <param name="result">The ErrorOr success result indicating success or the errors to report.</param>
    /// <param name="cancellation">The token used to cancel writing the response.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task SendSuccessAsync<TRequest, TResponse>(
        this Endpoint<TRequest, TResponse> endpoint,
        ErrorOr<Success> result,
        CancellationToken cancellation = default)
        where TRequest : notnull
    {
        if (!result.IsError)
        {
            await endpoint.HttpContext.Response.SendOkAsync(cancellation).ConfigureAwait(false);
            return;
        }

        await SendProblemAsync(endpoint.HttpContext, result.Errors, cancellation).ConfigureAwait(false);
    }

    /// <summary>
    /// Sends either a created response or problem details for an ErrorOr result.
    /// </summary>
    /// <typeparam name="TRequest">The type of the endpoint's request model.</typeparam>
    /// <typeparam name="TResponse">The type of the successful response value.</typeparam>
    /// <param name="endpoint">The endpoint whose HTTP context is used to write the response.</param>
    /// <param name="result">The ErrorOr result containing either the created response value or the errors to report.</param>
    /// <param name="locationFactory">A delegate that builds the Location header value from the created response value.</param>
    /// <param name="cancellation">The token used to cancel writing the response.</param>
    /// <returns><placeholder>A <see cref="Task"/> representing the asynchronous operation.</placeholder></returns>
    public static async Task SendCreatedAsync<TRequest, TResponse>(
        this Endpoint<TRequest, TResponse> endpoint,
        ErrorOr<TResponse> result,
        Func<TResponse, string> locationFactory,
        CancellationToken cancellation = default)
        where TRequest : notnull
    {
        if (!result.IsError)
        {
            endpoint.HttpContext.Response.Headers.Location = locationFactory(result.Value);
            endpoint.HttpContext.Response.StatusCode = StatusCodes.Status201Created;
            await endpoint.HttpContext.Response.WriteAsJsonAsync(result.Value, cancellationToken: cancellation).ConfigureAwait(false);
            return;
        }

        await SendProblemAsync(endpoint.HttpContext, result.Errors, cancellation).ConfigureAwait(false);
    }

    private static async Task SendProblemAsync(HttpContext httpContext, List<Error> errors, CancellationToken cancellation)
    {
        string traceId = httpContext.TraceIdentifier;

        if (errors.TrueForAll(error => error.Type == ErrorType.Validation))
        {
            var validationProblem = new ValidationProblemDetails(
                errors.GroupBy(error => error.Code, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Description).ToArray()))
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest,
                Instance = httpContext.Request.Path,
            };

            validationProblem.Extensions["traceId"] = traceId;

            httpContext.Response.StatusCode = StatusCodes.Status400BadRequest;
            await httpContext.Response.WriteAsJsonAsync(validationProblem, cancellationToken: cancellation).ConfigureAwait(false);
            return;
        }

        Error primaryError = errors[0];
        int statusCode = primaryError.Type switch
        {
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };

        var problemDetails = new Microsoft.AspNetCore.Mvc.ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = Enum.GetName<ErrorType>(primaryError.Type),
            Status = statusCode,
            Instance = httpContext.Request.Path,
        };

        problemDetails.Extensions["traceId"] = traceId;
        problemDetails.Extensions["errors"] = errors
            .Select(error => new { name = error.Code, reason = error.Description })
            .ToArray();

        httpContext.Response.StatusCode = statusCode;
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken: cancellation).ConfigureAwait(false);
    }
}
