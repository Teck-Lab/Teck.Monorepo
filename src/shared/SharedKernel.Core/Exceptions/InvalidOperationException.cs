using System.Net;

namespace SharedKernel.Core.Exceptions
{
    /// <summary>
    /// The invalid operation exception.
    /// </summary>
    public class InvalidOperationException : CustomException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidOperationException"/> class.
        /// </summary>
        public InvalidOperationException() : this(string.Empty)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidOperationException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public InvalidOperationException(string message) : base(message, HttpStatusCode.InternalServerError)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidOperationException"/> class with a specified error message and a reference to the inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public InvalidOperationException(string message, Exception innerException) : base(message, innerException)
        {
        }
    }
}
