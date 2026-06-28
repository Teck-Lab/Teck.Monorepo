using System.Reflection;
using SharedKernel.Core.CQRS;

namespace Teck.Platform.Arch.Tests.Rules;

/// <summary>
/// Reflection helpers for inspecting WolverineFx-style handlers. Handlers are static classes
/// exposing a public static <c>Handle</c> method and do <em>not</em> implement
/// <see cref="ICommandHandler{TCommand, TResponse}"/> / <see cref="IQueryHandler{TQuery, TResponse}"/>,
/// so ArchUnit interface selectors cannot find them. Classification is therefore done by inspecting
/// the <c>Handle</c> method's parameters: the message parameter implements <see cref="ICommand{T}"/>
/// (command handler) or <see cref="IQuery{T}"/> (query handler); anything else (e.g. an integration
/// event) is neither.
/// </summary>
internal static class HandlerReflection
{
    /// <summary>Gets the static handler classes (static class with a public static <c>Handle</c> method).</summary>
    /// <param name="applicationAssembly">The application assembly to scan.</param>
    /// <returns>The handler classes discovered in the assembly.</returns>
    internal static IReadOnlyList<Type> GetHandlerClasses(Assembly applicationAssembly) =>
        applicationAssembly
            .GetTypes()
            .Where(type =>
                type.IsClass
                && type.IsAbstract
                && type.IsSealed
                && type.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(method => method.Name == "Handle"))
            .ToArray();

    /// <summary>Gets the distinct parameter types across all <c>Handle</c> methods of a handler.</summary>
    /// <param name="handler">The handler type.</param>
    /// <returns>The parameter types.</returns>
    internal static Type[] HandleParameterTypes(Type handler) =>
        handler
            .GetMethods(BindingFlags.Public | BindingFlags.Static)
            .Where(method => method.Name == "Handle")
            .SelectMany(method => method.GetParameters())
            .Select(parameter => parameter.ParameterType)
            .ToArray();

    /// <summary>Determines whether the handler's message parameter implements <see cref="ICommand{T}"/>.</summary>
    /// <param name="handler">The handler type.</param>
    /// <returns><see langword="true"/> if the handler handles a command.</returns>
    internal static bool IsCommandHandler(Type handler) =>
        HandleParameterTypes(handler).Any(type => ImplementsOpenGeneric(type, typeof(ICommand<>)));

    /// <summary>Determines whether the handler's message parameter implements <see cref="IQuery{T}"/>.</summary>
    /// <param name="handler">The handler type.</param>
    /// <returns><see langword="true"/> if the handler handles a query.</returns>
    internal static bool IsQueryHandler(Type handler) =>
        HandleParameterTypes(handler).Any(type => ImplementsOpenGeneric(type, typeof(IQuery<>)));

    /// <summary>Determines whether the handler depends on the given open repository interface.</summary>
    /// <param name="handler">The handler type.</param>
    /// <param name="openRepositoryInterface">The open generic repository interface definition.</param>
    /// <returns><see langword="true"/> if a <c>Handle</c> parameter is that closed repository interface.</returns>
    internal static bool DependsOnRepository(Type handler, Type openRepositoryInterface) =>
        HandleParameterTypes(handler).Any(type => IsClosedGenericOf(type, openRepositoryInterface));

    // Compares open generic definitions so that IGenericWriteRepository (which derives from
    // IGenericReadRepository) is never mistaken for a read dependency, and vice versa.
    private static bool ImplementsOpenGeneric(Type type, Type openInterface) =>
        (type.IsGenericType && type.GetGenericTypeDefinition() == openInterface)
        || type.GetInterfaces().Any(i => i.IsGenericType && i.GetGenericTypeDefinition() == openInterface);

    private static bool IsClosedGenericOf(Type type, Type openGenericDefinition) =>
        type.IsGenericType && type.GetGenericTypeDefinition() == openGenericDefinition;
}
