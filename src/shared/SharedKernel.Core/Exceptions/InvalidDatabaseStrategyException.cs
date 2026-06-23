using System.Net;

namespace SharedKernel.Core.Exceptions
{
    /// <summary>
    /// Exception thrown when a tenant has an invalid or unassigned database strategy.
    /// </summary>
    public class InvalidDatabaseStrategyException : CustomException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidDatabaseStrategyException"/> class.
        /// </summary>
        public InvalidDatabaseStrategyException()
            : this("Tenant database strategy is invalid or unassigned.")
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidDatabaseStrategyException"/> class with a specified error message.
        /// </summary>
        /// <param name="message">The error message.</param>
        public InvalidDatabaseStrategyException(string message)
            : base(message, HttpStatusCode.InternalServerError)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="InvalidDatabaseStrategyException"/> class with a specified error message and a reference to the inner exception.
        /// </summary>
        /// <param name="message">The error message.</param>
        /// <param name="innerException">The inner exception.</param>
        public InvalidDatabaseStrategyException(string message, Exception innerException)
            : base(message, innerException)
        {
        }
    }
}
