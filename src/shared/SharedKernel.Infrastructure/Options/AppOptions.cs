using System.ComponentModel.DataAnnotations;
using SharedKernel.Core.Options;

namespace SharedKernel.Infrastructure.Options
{
    /// <summary>
    /// The app options.
    /// </summary>
    public class AppOptions : IOptionsRoot
    {
        /// <summary>
        /// Appsettings name.
        /// </summary>
        public const string Section = "App";

        /// <summary>
        /// Gets or sets the name.
        /// </summary>
        [Required(AllowEmptyStrings = false)]
        public string Name { get; set; } = "Teck.Cloud.WebAPI";

        /// <summary>
        /// Gets or sets the description.
        /// </summary>
        public string Description { get; set; } = string.Empty;

        /// <summary>
        /// Gets the API versions. Defaults to [1].
        /// </summary>
        public ICollection<int> Versions { get; } = [1];
    }
}
