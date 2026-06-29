namespace Gateway.Public.Edge;

/// <summary>An edge problem mapped to RFC-7807 output.</summary>
/// <param name="StatusCode">The HTTP status code.</param>
/// <param name="Title">The problem title.</param>
/// <param name="Detail">The human-readable detail.</param>
/// <param name="ErrorCode">The stable machine error code.</param>
public sealed record EdgeProblem(int StatusCode, string Title, string Detail, string ErrorCode);
