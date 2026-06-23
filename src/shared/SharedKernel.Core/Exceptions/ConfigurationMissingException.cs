using System.Net;

namespace SharedKernel.Core.Exceptions
{
    /// <summary>
    /// The configuration missing exception.
    /// </summary>
    public class ConfigurationMissingException : CustomException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationMissingException"/> class with the specified section name.
        /// </summary>
        /// <param name="sectionName">The name of the missing configuration section.</param>
        public ConfigurationMissingException(string sectionName)
            : base($"{sectionName} Missing in Configurations", HttpStatusCode.NotFound)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationMissingException"/> class.
        /// </summary>
        public ConfigurationMissingException()
            : base("Configuration missing", HttpStatusCode.NotFound)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ConfigurationMissingException"/> class with a specified error message and a reference to the inner exception that is the cause of this exception.
        /// </summary>
        /// <param name="message">The error message that explains the reason for the exception.</param>
        /// <param name="innerException">The exception that is the cause of the current exception.</param>
        public ConfigurationMissingException(string message, Exception innerException)
            : base(message, HttpStatusCode.InternalServerError)
        {
        }
    }
}
