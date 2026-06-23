using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.CQRS;

namespace Teck.Platform.Arch.Tests.Rules;

public static class CommandHandlerRules
{
    public static void CommandHandlersShouldBeSealed(Architecture architecture)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Should()
            .BeSealed()
            .Because("command handlers should be sealed to prevent inheritance")
            .WithoutRequiringPositiveResults();

        rule.Check(architecture);
    }

    public static void CommandHandlersShouldResideInFeaturesNamespace(Architecture architecture, string applicationRootNamespace)
    {
        if (string.IsNullOrWhiteSpace(applicationRootNamespace))
            applicationRootNamespace = string.Empty;
        applicationRootNamespace = applicationRootNamespace.Trim().TrimEnd('.');

        var escaped = System.Text.RegularExpressions.Regex.Escape(applicationRootNamespace);
        var pattern = "^" + escaped + @"(?:\.[A-Za-z0-9_]+)*\.Features(?:\..*)?$";

        ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Should()
            .ResideInNamespaceMatching(pattern)
            .Because("command handlers should be organized in a Features folder (possibly nested)")
            .Check(architecture);
    }

    public static void CommandHandlersShouldResideInApplicationAssembly(Architecture architecture, System.Reflection.Assembly applicationAssembly)
    {
        // If an assembly name is provided, require command handlers to be defined in that assembly
        // Use a tolerant rule so it doesn't fail shared test runs that don't load application assemblies
        ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Should()
            .ResideInAssembly(applicationAssembly)
            .Because("command handlers should be defined in the application assembly")
            .WithoutRequiringPositiveResults()
            .Check(architecture);
    }

    public static void CommandsShouldBeImmutable(Architecture architecture)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .AreAssignableTo(typeof(ICommand<>))
            .Should()
            .BeImmutable()
            .Because("commands should be immutable to prevent state changes");

        rule.Check(architecture);
    }

    public static void CommandHandlersShouldHaveCorrectName(Architecture architecture)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Should()
            .HaveNameEndingWith("CommandHandler")
            .Because("command handlers should follow naming convention");

        rule.Check(architecture);
    }

    public static void CommandHandlersShouldNotBePublic(Architecture architecture)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Should()
            .NotBePublic()
            .Because("command handlers should be internal for better encapsulation");

        rule.Check(architecture);
    }

    public static void CommandHandlersShouldUseWriteRepositories(Architecture architecture)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(ICommandHandler<,>))
            .Or()
            .ImplementInterface(typeof(ICommandHandler<>))
            .Should()
            .DependOnAny(ArchRuleDefinition.Interfaces().That().HaveNameEndingWith("WriteRepository"))
            .Because("command handlers should use write repositories")
            .WithoutRequiringPositiveResults();

        rule.Check(architecture);
    }

    // Note: We don't enforce that command handlers can't use read repositories
    // Command handlers may need to use read repositories for validation or lookups
}
