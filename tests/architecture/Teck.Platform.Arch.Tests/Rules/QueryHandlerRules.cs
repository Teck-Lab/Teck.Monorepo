using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.CQRS;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Teck.Platform.Arch.Tests.Rules;

public static class QueryHandlerRules
{
    public static void QueryHandlersShouldBeSealed(Architecture architecture)
    {
        var rule = Classes()
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .BeSealed()
            .Because("query handlers should be sealed to prevent inheritance")
            .WithoutRequiringPositiveResults();

        rule.Check(architecture);
    }

    public static void QueryHandlersShouldBeReadOnly(Architecture architecture)
    {
        var rule = Classes()
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .BeImmutable()
            .Because("query handlers should not modify state");

        rule.Check(architecture);
    }

    public static void QueriesShouldNotModifyState(Architecture architecture)
    {
        var rule = Classes()
            .That()
            .ImplementInterface(typeof(IQuery<>))
            .Should()
            .BeImmutable()
            .Because("queries should be immutable");

        rule.Check(architecture);
    }

    public static void QueryHandlersShouldHaveCorrectName(Architecture architecture)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .HaveNameEndingWith("QueryHandler")
            .Because("query handlers should follow naming convention");

        rule.Check(architecture);
    }

    public static void QueryHandlersShouldNotBePublic(Architecture architecture)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .NotBePublic()
            .Because("query handlers should be internal for better encapsulation");

        rule.Check(architecture);
    }

    public static void QueryHandlersShouldUseReadRepositories(Architecture architecture)
    {
        var allowedInterfaces = ArchRuleDefinition.Interfaces()
            .That()
            .HaveNameEndingWith("ReadRepository")
            .Or()
            .HaveNameEndingWith("Cache")
            .Or()
            .HaveNameEndingWith("Runner");

        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .DependOnAny(allowedInterfaces)
            .Because("query handlers should use read repositories, caches, or runners")
            .WithoutRequiringPositiveResults();

        rule.Check(architecture);
    }

    public static void QueryHandlersShouldNotUseWriteRepositories(Architecture architecture)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(IQueryHandler<,>))
            .Should()
            .NotDependOnAny(ArchRuleDefinition.Interfaces().That().HaveNameEndingWith("WriteRepository"))
            .Because("query handlers should not use write repositories")
            .WithoutRequiringPositiveResults();

        rule.Check(architecture);
    }
}
