namespace SharedKernel.Infrastructure.Auth;

/// <summary>
/// Represents a token exchange failure with an optional mapped HTTP status.
/// </summary>
public sealed class TokenExchangeException : Exception
{
    /// <summary>
    /// Initializes a new instance of the <see cref="TokenExchangeException"/> class.
    /// </summary>
    public TokenExchangeException()
        : base("Token exchange failed.")
    {
        Error = "unknown_error";
        Description = "n/a";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenExchangeException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    public TokenExchangeException(string message)
        : base(message)
    {
        Error = "unknown_error";
        Description = "n/a";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenExchangeException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="innerException">Inner exception.</param>
    public TokenExchangeException(string message, Exception innerException)
        : base(message, innerException)
    {
        Error = "unknown_error";
        Description = "n/a";
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="TokenExchangeException"/> class.
    /// </summary>
    /// <param name="message">Exception message.</param>
    /// <param name="error">Identity provider error code.</param>
    /// <param name="description">Identity provider error description.</param>
    /// <param name="statusCode">Mapped HTTP status code.</param>
    /// <param name="isAuthFailure">Whether the failure is authorization/authentication related.</param>
    public TokenExchangeException(
        string message,
        string error,
        string description,
        int statusCode,
        bool isAuthFailure)
        : base(message)
    {
        Error = error;
        Description = description;
        StatusCode = statusCode;
        IsAuthFailure = isAuthFailure;
    }

    /// <summary>
    /// Gets the identity provider error code.
    /// </summary>
    public string Error { get; }

    /// <summary>
    /// Gets the identity provider error description.
    /// </summary>
    public string Description { get; }

    /// <summary>
    /// Gets the mapped HTTP status code for the failure.
    /// </summary>
    public int StatusCode { get; }

    /// <summary>
    /// Gets a value indicating whether the error represents an auth failure.
    /// </summary>
    public bool IsAuthFailure { get; }
}
