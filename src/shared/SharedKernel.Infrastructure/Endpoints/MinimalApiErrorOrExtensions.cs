using System.Diagnostics.CodeAnalysis;
using ErrorOr;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace SharedKernel.Infrastructure.Endpoints;

/// <summary>
/// Provides helpers to map ErrorOr results to Minimal API IResult responses.
/// </summary>
public static class MinimalApiErrorOrExtensions
{
    /// <summary>
    /// Converts a collection of <see cref="Error"/> instances into a Minimal API error response.
    /// </summary>
    /// <param name="errors">The error collection.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>An <see cref="IResult"/> that contains ProblemDetails.</returns>
    public static IResult ToMinimalApiErrorResult(this IEnumerable<Error> errors, HttpContext httpContext)
    {
        List<Error> errorList = errors.ToList();
        if (errorList.Count == 0)
        {
            throw new InvalidOperationException("No errors were provided.");
        }

        ErrorOr<object> response = errorList;
        return CreateErrorResult(response, httpContext);
    }

    /// <summary>
    /// Converts an <see cref="ErrorOr{TValue}"/> result into a Minimal API response.
    /// </summary>
    /// <typeparam name="TValue">The success value type.</typeparam>
    /// <param name="result">The operation result.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>An <see cref="IResult"/> representing success or failure.</returns>
    public static IResult ToMinimalApiResult<TValue>(this ErrorOr<TValue> result, HttpContext httpContext)
    {
        if (!result.IsError)
        {
            return Results.Ok(result.Value);
        }

        return CreateErrorResult(result, httpContext);
    }

    /// <summary>
    /// Converts an <see cref="ErrorOr{TValue}"/> result into a created response or an error response.
    /// </summary>
    /// <typeparam name="TValue">The success value type.</typeparam>
    /// <param name="result">The operation result.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <param name="location">The resource location for successful creation.</param>
    /// <returns>An <see cref="IResult"/> representing success or failure.</returns>
    public static IResult ToCreatedMinimalApiResult<TValue>(
        this ErrorOr<TValue> result,
        HttpContext httpContext,
        string location)
    {
        if (!result.IsError)
        {
            Uri locationUri = Uri.TryCreate(location, UriKind.RelativeOrAbsolute, out Uri? parsedLocation)
                ? parsedLocation
                : new Uri(location, UriKind.Relative);

            return Results.Created(locationUri, result.Value);
        }

        return CreateErrorResult(result, httpContext);
    }

    /// <summary>
    /// Converts an <see cref="ErrorOr{Deleted}"/> result into a no-content response or an error response.
    /// </summary>
    /// <param name="result">The operation result.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>An <see cref="IResult"/> representing success or failure.</returns>
    public static IResult ToNoContentMinimalApiResult(this ErrorOr<Deleted> result, HttpContext httpContext)
    {
        if (!result.IsError)
        {
            return Results.NoContent();
        }

        return CreateErrorResult(result, httpContext);
    }

    /// <summary>
    /// Converts an <see cref="ErrorOr{TValue}"/> result into a no-content response or an error response.
    /// </summary>
    /// <typeparam name="TValue">The success value type.</typeparam>
    /// <param name="result">The operation result.</param>
    /// <param name="httpContext">The current HTTP context.</param>
    /// <returns>An <see cref="IResult"/> representing success or failure.</returns>
    public static IResult ToNoContentMinimalApiResult<TValue>(this ErrorOr<TValue> result, HttpContext httpContext)
    {
        if (!result.IsError)
        {
            return Results.NoContent();
        }

        return CreateErrorResult(result, httpContext);
    }

    [RequiresDynamicCode("Calls Microsoft.AspNetCore.Http.Results.Json<TValue>(TValue, JsonSerializerOptions, String, Int32?)")]
    private static IResult CreateErrorResult(IErrorOr response, HttpContext http)
    {
        string traceId = http.TraceIdentifier;

        if (response.Errors?.TrueForAll(error => error.Type == ErrorType.Validation) == true)
        {
            var problemDetails = new ValidationProblemDetails(
                response.Errors
                    .GroupBy(error => error.Code, StringComparer.Ordinal)
                    .ToDictionary(
                        group => group.Key,
                        group => group.Select(error => error.Description).ToArray()))
            {
                Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
                Title = "One or more validation errors occurred.",
                Status = StatusCodes.Status400BadRequest,
                Instance = http.Request.Path,
            };

            problemDetails.Extensions["traceId"] = traceId;

            return Results.Json(problemDetails, statusCode: StatusCodes.Status400BadRequest, contentType: "application/problem+json");
        }

        List<Error> nonValidationErrors = response.Errors?
            .Where(candidate => candidate.Type != ErrorType.Validation)
            .ToList() ?? [];

        if (nonValidationErrors.Count == 0)
        {
            throw new InvalidOperationException("No matching endpoint error.");
        }

        Error primaryError = nonValidationErrors[0];

        int statusCode = primaryError.Type switch
        {
            ErrorType.Conflict => StatusCodes.Status409Conflict,
            ErrorType.NotFound => StatusCodes.Status404NotFound,
            ErrorType.Unauthorized => StatusCodes.Status401Unauthorized,
            ErrorType.Forbidden => StatusCodes.Status403Forbidden,
            _ => StatusCodes.Status400BadRequest,
        };

        var genericProblem = new ProblemDetails
        {
            Type = "https://tools.ietf.org/html/rfc7231#section-6.5.1",
            Title = Enum.GetName<ErrorType>(primaryError.Type),
            Status = statusCode,
            Instance = http.Request.Path,
        };

        genericProblem.Extensions["traceId"] = traceId;
        genericProblem.Extensions["errors"] = nonValidationErrors
            .Select(error => new { name = error.Code, reason = error.Description })
            .ToArray();

        return Results.Json(genericProblem, statusCode: statusCode, contentType: "application/problem+json");
    }
}
