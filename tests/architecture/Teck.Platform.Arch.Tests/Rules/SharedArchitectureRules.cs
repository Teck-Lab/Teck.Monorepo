using ArchUnitNET.Domain;
using Assembly = System.Reflection.Assembly;

namespace Teck.Platform.Arch.Tests.Rules;

/// <summary>
/// Single entry point that runs every shared architecture rule against a service. Each service's
/// architecture test calls <see cref="AssertAll"/> so the rule set is defined once and enforced
/// identically across services.
/// </summary>
public static class SharedArchitectureRules
{
    /// <summary>Runs all shared architecture rules against a service.</summary>
    /// <param name="architecture">The loaded service architecture (Domain + Application + Host).</param>
    /// <param name="applicationAssembly">The service's Application assembly (where handlers live).</param>
    public static void AssertAll(Architecture architecture, Assembly applicationAssembly)
    {
        // CQRS handlers (WolverineFx static Handle methods).
        CommandHandlerRules.CommandHandlersShouldBeStaticClassesEndingWithHandler(applicationAssembly);
        CommandHandlerRules.CommandHandlersShouldResideInFeaturesNamespace(applicationAssembly);
        CommandHandlerRules.CommandHandlersShouldNotUseReadRepositories(applicationAssembly);
        CommandHandlerRules.CommandsShouldBeImmutable(architecture);

        QueryHandlerRules.QueryHandlersShouldBeStaticClassesEndingWithHandler(applicationAssembly);
        QueryHandlerRules.QueryHandlersShouldResideInFeaturesNamespace(applicationAssembly);
        QueryHandlerRules.QueryHandlersShouldNotUseWriteRepositories(applicationAssembly);
        QueryHandlerRules.QueriesShouldNotModifyState(architecture);

        // Domain model.
        DomainRules.EntitiesShouldInheritBaseEntity(architecture);
        DomainRules.AggregateRootsShouldBeInDomainLayer(architecture);
        DomainRules.EntitiesShouldHavePrivateSetters(architecture);
        DomainRules.EntityCreateMethodsShouldBeStatic(architecture);

        AggregateRootRules.AggregatesShouldInheritFromBaseEntity(architecture);
        AggregateRootRules.AggregatesShouldResideInNamespace(architecture, "Domain.Entities");

        // Domain events / validators — tolerant of zero matches during early scaffolding, so they
        // enforce the convention as soon as the first such type appears.
        DomainEventRules.DomainEventsShouldBeSealed(architecture);
        DomainEventRules.DomainEventsShouldResideInDomainEventsNamespace(architecture);
        ValidationRules.ValidatorsShouldBeSealed(architecture);
    }
}
