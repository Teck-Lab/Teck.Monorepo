using System.Net;

namespace SharedKernel.Core.Exceptions;

/// <summary>
/// The invalid transaction exception.
/// </summary>
public class InvalidTransactionException : CustomException
{
    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTransactionException"/> class.
    /// </summary>
    public InvalidTransactionException() : base(string.Empty, HttpStatusCode.InternalServerError)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTransactionException"/> class with a specified error message.
    /// </summary>
    /// <param name="message">The error message.</param>
    public InvalidTransactionException(string message) : base(message, HttpStatusCode.InternalServerError)
    {
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="InvalidTransactionException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
    /// </summary>
    /// <param name="message">The error message.</param>
    /// <param name="innerException">The inner exception.</param>
    public InvalidTransactionException(string message, Exception innerException) : base(message, innerException)
    {
    }
}
