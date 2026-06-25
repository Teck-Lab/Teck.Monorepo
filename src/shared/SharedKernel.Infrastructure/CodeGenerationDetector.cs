using System.Reflection;

namespace SharedKernel.Infrastructure;

/// <summary>
/// Detects build-time application execution modes such as Wolverine code generation.
/// </summary>
public static class CodeGenerationDetector
{
    /// <summary>
    /// Returns true when the current process is running source generation instead of the normal application host.
    /// </summary>
    /// <returns></returns>
    public static bool IsRunningGeneration()
    {
        return IsRunningOpenApiGeneration() || IsRunningWolverineCodeGeneration();
    }

    /// <summary>
    /// Returns true when the current process is running Wolverine code generation commands.
    /// </summary>
    /// <returns></returns>
    public static bool IsRunningWolverineCodeGeneration()
    {
        string[] commandLineArgs = Environment.GetCommandLineArgs();
        return commandLineArgs.Any(arg => string.Equals(arg, "codegen", StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Returns true when the current process is running OpenAPI document generation.
    /// </summary>
    /// <returns></returns>
    public static bool IsRunningOpenApiGeneration()
    {
        string? entryAssemblyName = Assembly.GetEntryAssembly()?.GetName().Name;
        return string.Equals(entryAssemblyName, "GetDocument.Insider", StringComparison.Ordinal);
    }
}
