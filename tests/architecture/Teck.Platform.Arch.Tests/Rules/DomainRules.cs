using System.Reflection;
using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;

namespace Teck.Platform.Arch.Tests.Rules;

public static class DomainRules
{
    public static void EntitiesShouldInheritBaseEntity(Architecture architecture)
    {
        var rule = ArchRuleDefinition.Classes()
            .That()
            .ImplementInterface(typeof(IBaseEntity))
            .Should()
            .BeAssignableTo(typeof(BaseEntity))
            .Because("entities should inherit from BaseEntity");

        rule.Check(architecture);
    }

    // NOTE: A previous "AggregatesShouldTrackDomainEvents" rule was removed here. It asserted that
    // each aggregate declares a `_domainEvents` field and `AddDomainEvent`/`ClearDomainEvents`/
    // `GetDomainEvents` members. Those members are provided by the `BaseEntity<TId>` base class (and
    // the getter is a `DomainEvents` property, not a `GetDomainEvents` method), so the rule both
    // checked for a member that does not exist and duplicated the inherit-from-BaseEntity guarantee
    // already enforced by EntitiesShouldInheritBaseEntity / AggregatesShouldInheritFromBaseEntity.

    public static void EntitiesShouldHavePrivateSetters(Architecture architecture)
    {
        var rule = ArchRuleDefinition.Members()
            .That()
            .HaveNameStartingWith("set_")
            .And()
            .AreDeclaredIn(ArchRuleDefinition.Classes().That().ImplementInterface(typeof(IBaseEntity)))
            // TenantId is a public-settable property mandated by the ITenantScoped contract (the EF
            // SaveChanges tenant interceptor assigns it), so it is exempt from the private-setter rule.
            // (Member names carry their parameter signature, e.g. "set_TenantId(System.String)", so
            // match by substring rather than exact name.)
            .And()
            .DoNotHaveNameContaining("set_TenantId")
            .Should()
            .NotBePublic()
            .Because("entity properties should have private setters for encapsulation")
            .WithoutRequiringPositiveResults();
        rule.Check(architecture);
    }

    public static void AggregateRootsShouldBeInDomainLayer(Architecture architecture)
    {
        var rule = ArchRuleDefinition.Classes()
            .That()
            .ImplementInterface(typeof(IAggregateRoot))
            .Should()
            .ResideInNamespaceMatching("Domain.Entities")
            .Because("aggregate roots should be in the domain layer");

        rule.Check(architecture);
    }

    public static void EntityCreateMethodsShouldBeStatic(Architecture architecture)
    {
        var entityTypes = architecture.Classes
            .Select(type => type.Name)
            .ToHashSet(StringComparer.Ordinal);
        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            foreach (var type in assembly.GetTypes().Where(type =>
                type.FullName is not null && entityTypes.Contains(type.FullName) &&
                typeof(IBaseEntity).IsAssignableFrom(type)))
            {
                foreach (var method in type.GetMethods(BindingFlags.Public | BindingFlags.NonPublic |
                    BindingFlags.Instance | BindingFlags.Static | BindingFlags.DeclaredOnly)
                    .Where(method => method.Name.StartsWith("Create", StringComparison.Ordinal)))
                {
                    Xunit.Assert.True(method.IsStatic, $"Entity factory method '{method}' must be static.");
                }
            }
        }
    }
}
