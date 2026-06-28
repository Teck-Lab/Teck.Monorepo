using ArchUnitNET.Domain;
using ArchUnitNET.Fluent;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Events;

namespace Teck.Platform.Arch.Tests.Rules;

public static class DomainEventRules
{
    public static void DomainEventsShouldBeSealed(Architecture architecture)
    {
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .Should()
            .BeSealed()
            .Because("domain events should be sealed to prevent inheritance")
            .WithoutRequiringPositiveResults();

        rule.Check(architecture);
    }

    public static void DomainEventsShouldResideInDomainEventsNamespace(Architecture architecture)
    {
        // The codebase names domain events by namespace (e.g. Orders.Domain.DomainEvents.OrderPlaced),
        // not by a "DomainEvent" suffix, so the convention enforced here is the namespace.
        var rule = ArchRuleDefinition
            .Classes()
            .That()
            .ImplementInterface(typeof(IDomainEvent))
            .Should()
            .ResideInNamespaceMatching("Domain.DomainEvents")
            .Because("domain events should live in the Domain.DomainEvents namespace")
            .WithoutRequiringPositiveResults();

        rule.Check(architecture);
    }
}
