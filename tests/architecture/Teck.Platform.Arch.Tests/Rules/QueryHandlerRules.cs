using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.CQRS;
using SharedKernel.Core.Database;
using Xunit;
using Assembly = System.Reflection.Assembly;

namespace Teck.Platform.Arch.Tests.Rules;

/// <summary>
/// Architecture rules for query handlers. Handlers are WolverineFx static <c>Handle</c> methods,
/// so they are discovered reflectively and classified by their <see cref="IQuery{T}"/> parameter
/// rather than by a handler interface (which is never implemented).
/// </summary>
public static class QueryHandlerRules
{
    /// <summary>Query handlers must be static classes whose name ends with <c>Handler</c>.</summary>
    /// <param name="applicationAssembly">The application assembly to scan.</param>
    public static void QueryHandlersShouldBeStaticClassesEndingWithHandler(Assembly applicationAssembly)
    {
        string[] violations = HandlerReflection.GetHandlerClasses(applicationAssembly)
            .Where(HandlerReflection.IsQueryHandler)
            .Where(handler => !handler.Name.EndsWith("Handler", StringComparison.Ordinal))
            .Select(handler => handler.Name)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Query handlers must be static classes named '...Handler': " + string.Join(", ", violations));
    }

    /// <summary>Query handlers must live under a <c>.Features.</c> namespace.</summary>
    /// <param name="applicationAssembly">The application assembly to scan.</param>
    public static void QueryHandlersShouldResideInFeaturesNamespace(Assembly applicationAssembly)
    {
        string[] violations = HandlerReflection.GetHandlerClasses(applicationAssembly)
            .Where(HandlerReflection.IsQueryHandler)
            .Where(handler => handler.Namespace is null || !handler.Namespace.Contains(".Features.", StringComparison.Ordinal))
            .Select(handler => handler.FullName!)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Query handlers must reside under a '.Features.' namespace: " + string.Join(", ", violations));
    }

    /// <summary>
    /// Query handlers must never depend on a write repository — the read side stays read-only so the
    /// read context can target a separate read database/replica.
    /// </summary>
    /// <param name="applicationAssembly">The application assembly to scan.</param>
    public static void QueryHandlersShouldNotUseWriteRepositories(Assembly applicationAssembly)
    {
        string[] violations = HandlerReflection.GetHandlerClasses(applicationAssembly)
            .Where(HandlerReflection.IsQueryHandler)
            .Where(handler => HandlerReflection.DependsOnRepository(handler, typeof(IGenericWriteRepository<,>)))
            .Select(handler => handler.Name)
            .ToArray();

        Assert.True(
            violations.Length == 0,
            "Query handlers must not depend on a write repository: " + string.Join(", ", violations));
    }

    /// <summary>Queries must be immutable.</summary>
    /// <param name="architecture">The loaded architecture.</param>
    public static void QueriesShouldNotModifyState(Architecture architecture) =>
        ArchRuleDefinition
            .Classes()
            .That()
            .AreAssignableTo(typeof(IQuery<>))
            .Should()
            .BeImmutable()
            .Because("queries should be immutable")
            .WithoutRequiringPositiveResults()
            .Check(architecture);
}
