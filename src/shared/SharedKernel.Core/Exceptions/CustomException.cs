using System.Net;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// The custom exception.
/// </summary>
public class CustomException : Exception
{
    /// <summary>
    /// Gets the status code.
    /// </summary>
    public HttpStatusCode StatusCode { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomException"/> class.
    /// </summary>
    public CustomException() : this(string.Empty, HttpStatusCode.InternalServerError)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public CustomException(string message) : this(message, HttpStatusCode.InternalServerError)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomException"/> class with a specified error message and inner exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public CustomException(string message, Exception innerException) : base(message, innerException)
    {
        StatusCode = HttpStatusCode.InternalServerError;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="CustomException"/> class with a specified error message and status code.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="statusCode">The HTTP status code.</param>
    public CustomException(string message, HttpStatusCode statusCode) : base(message)
    {
        StatusCode = statusCode;
    }
}
