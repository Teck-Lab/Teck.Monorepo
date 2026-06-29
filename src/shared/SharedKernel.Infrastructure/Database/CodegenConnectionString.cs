using Microsoft.Extensions.Configuration;

namespace SharedKernel.Infrastructure.Database;

/// <summary>
/// Resolves required database connection strings, tolerating their absence when the process
/// is started for a build-time command such as WolverineFx <c>codegen write</c>.
/// </summary>
/// <remarks>
/// The <c>codegen write</c> step builds the dependency-injection container so WolverineFx can
/// discover handler dependencies and emit generated code, but it never opens a database
/// connection. Requiring real connection strings at that point would force the container build
/// to supply secrets it does not need. This resolver returns a syntactically valid but
/// non-functional placeholder during code generation and otherwise enforces that a real value
/// is configured.
/// </remarks>
public static class CodegenConnectionString
{
    /// <summary>
    /// A syntactically valid Npgsql connection string used only during code generation. It is
    /// never connected to; it exists so that <c>UseNpgsql(...)</c> registration succeeds when no
    /// real connection string is configured at build time.
    /// </summary>
    private const string Placeholder =
        "Host=localhost;Port=5432;Database=codegen;Username=codegen;Password=codegen";

    /// <summary>
    /// Returns the first non-empty connection string for the supplied <paramref name="names"/>.
    /// When none are configured, returns a build-time placeholder during code generation and
    /// otherwise throws.
    /// </summary>
    /// <param name="configuration">The application configuration to read connection strings from.</param>
    /// <param name="names">The connection-string names to probe, in priority order.</param>
    /// <returns>The resolved connection string, or a placeholder during code generation.</returns>
    /// <exception cref="ArgumentNullException">Thrown when <paramref name="configuration"/> is null.</exception>
    /// <exception cref="InvalidOperationException">
    /// Thrown when no connection string is configured and the process is not running code generation.
    /// </exception>
    public static string ResolveRequired(IConfiguration configuration, params string[] names)
    {
        ArgumentNullException.ThrowIfNull(configuration);

        foreach (string name in names ?? [])
        {
            string? value = configuration.GetConnectionString(name);
            if (!string.IsNullOrWhiteSpace(value))
            {
                return value;
            }
        }

        if (CodeGenerationDetector.IsRunningGeneration())
        {
            return Placeholder;
        }

        throw new InvalidOperationException(
            $"Missing connection string. Checked: {string.Join("/", names ?? [])}.");
    }
}
