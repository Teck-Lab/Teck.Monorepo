using System.ComponentModel.DataAnnotations;

namespace SharedKernel.Infrastructure.OpenApi
{
    /// <summary>
    /// OpenApi options.
    /// </summary>
    public class OpenApiOptions
    {
        /// <summary>
        /// OpenApi Options section.
        /// </summary>
        public const string Section = "OpenApi";

        /// <summary>
        /// Gets or sets the title.
        /// </summary>
        public string? Title { get; set; }

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        public string? Description { get; set; }

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        public string? Name { get; set; }

        /// <summary>
        /// Gets or sets the email.
        /// </summary>
        public string? Email { get; set; }

        /// <summary>
        /// Gets the api versions.
        /// </summary>
        [Required]
        public IList<int> ApiVersions { get; } = [];

        /// <summary>
        /// Gets or sets the url path.
        /// </summary>
        [Required]
        public Uri? UrlPath { get; set; }
    }
}
