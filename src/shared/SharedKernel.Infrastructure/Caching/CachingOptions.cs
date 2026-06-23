using SharedKernel.Core.Options;

namespace SharedKernel.Infrastructure.Caching
{
    /// <summary>
    /// The caching options.
    /// </summary>
    public class CachingOptions : IOptionsRoot
    {
        /// <summary>
        /// Gets or sets the redis URL.
        /// </summary>
        public Uri? RedisURL { get; set; }

        /// <summary>
        /// Gets or sets the password.
        /// </summary>
        public string? Password { get; set; }
    }
}
