using SharedKernel.Core.Options;

namespace SharedKernel.Infrastructure.Database;

/// <summary>
/// The database options.
/// </summary>
public class DatabaseOptions : IOptionsRoot
{
    /// <summary>
    /// Gets or sets the connection string.
    /// </summary>
    public string ConnectionString { get; set; } = null!;

    /// <summary>
    /// Gets or sets the database name.
    /// </summary>
    public string DatabaseName { get; set; } = null!;
}
