using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.CQRS;
using SharedKernel.Core.Database;
using Xunit;
using Assembly = System.Reflection.Assembly;

namespace Teck.Platform.Arch.Tests.Rules;

/// <summary>
/// Architecture rules for command handlers. Handlers are WolverineFx static <c>Handle</c> methods,
/// so they are discovered reflectively and classified by their <see cref="ICommand{T}"/> parameter
/// rather than by a handler interface (which is never implemented).
/// </summary>
public static class CommandHandlerRules
{
    /// <summary>Command handlers must be static classes whose name ends with <c>Handler</c>.</summary>
    /// <param name="applicationAssembly">The application assembly to scan.</param>
    public static void CommandHandlersShouldBeStaticClassesEndingWithHandler(Assembly applicationAssembly)
    {
        string[] violations = HandlerReflection.GetHandlerClasses(applicationAssembly)
            .Where(HandlerReflection.IsCommandHandler)
            .Where(handler => !handler.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(handler => handler.Name)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Command handlers must be static classes named '...Handler': " + string.Join(", ", violations));
    }

    /// <summary>Command handlers must live under a <c>.Features.</c> namespace.</summary>
    /// <param name="applicationAssembly">The application assembly to scan.</param>
    public static void CommandHandlersShouldResideInFeaturesNamespace(Assembly applicationAssembly)
    {
        string[] violations = HandlerReflection.GetHandlerClasses(applicationAssembly)
            .Where(HandlerReflection.IsCommandHandler)
            .Where(handler => handler.Namespace is null || !handler.Namespace.Contains(".Features.", StringComparison.Ordinal))
            .Select(handler => handler.FullName!)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Command handlers must reside under a '.Features.' namespace: " + string.Join(", ", violations));
    }

    /// <summary>
    /// Command handlers must depend on the write repository only — never the read repository.
    /// This enforces strict read/write context separation per handler so the read context stays
    /// free to target a separate read database/replica.
    /// </summary>
    /// <param name="applicationAssembly">The application assembly to scan.</param>
    public static void CommandHandlersShouldNotUseReadRepositories(Assembly applicationAssembly)
    {
        string[] violations = HandlerReflection.GetHandlerClasses(applicationAssembly)
            .Where(HandlerReflection.IsCommandHandler)
            .Where(handler => HandlerReflection.DependsOnRepository(handler, typeof(IGenericReadRepository<,>)))
            .Select(handler => handler.Name)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Command handlers must use the write repository only (read/write separation): " + string.Join(", ", violations));
    }

    /// <summary>Commands must be immutable.</summary>
    /// <param name="architecture">The loaded architecture.</param>
    public static void CommandsShouldBeImmutable(Architecture architecture) =>
        ArchRuleDefinition
            .Classes()
            .That()
            .AreAssignableTo(typeof(ICommand<>))
            .Should()
            .BeImmutable()
            .Because("commands should be immutable to prevent state changes")
            .WithoutRequiringPositiveResults()
            .Check(architecture);
}
