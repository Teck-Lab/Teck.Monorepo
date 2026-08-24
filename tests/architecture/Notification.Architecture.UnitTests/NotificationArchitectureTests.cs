using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;
using Teck.Platform.Arch.Tests.Rules;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Notifications.Architecture.UnitTests;

public sealed class NotificationArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly Assembly DomainAssembly = typeof(Notifications.Domain.Entities.NotificationDelivery).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Notifications.Application.Notifications.Features.QueueNotification.V1.QueueNotificationHandler).Assembly;
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;
    private static readonly ArchUnitNET.Domain.Architecture Architecture = new ArchLoader().LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly).Build();

    [Fact]
    public void Host_DoesNotReferenceDomainDirectly() => Types().That().ResideInAssembly(HostAssembly).Should().NotDependOnAny(Types().That().ResideInAssembly(DomainAssembly)).Check(Architecture);

    [Fact]
    public void Application_DoesNotReferenceHostOrConcretePersistence() => Types().That().ResideInAssembly(ApplicationAssembly).And().DoNotHaveFullNameContaining("DbContext").Should().NotDependOnAny(Types().That().HaveFullNameContaining("DbContext")).AndShould().NotDependOnAny(Types().That().ResideInAssembly(HostAssembly)).Check(Architecture);

    [Fact]
    public void NotificationAggregates_AreTenantScoped() => Classes().That().ImplementInterface(typeof(IAggregateRoot)).Should().ImplementInterface(typeof(ITenantScoped)).Check(Architecture);

    [Fact]
    public void Service_FollowsSharedArchitectureRules()
    {
        CommandHandlerRules.CommandHandlersShouldBeStaticClassesEndingWithHandler(ApplicationAssembly);
        CommandHandlerRules.CommandHandlersShouldResideInFeaturesNamespace(ApplicationAssembly);
        CommandHandlerRules.CommandHandlersShouldNotUseReadRepositories(ApplicationAssembly);
        QueryHandlerRules.QueryHandlersShouldBeStaticClassesEndingWithHandler(ApplicationAssembly);
        QueryHandlerRules.QueryHandlersShouldResideInFeaturesNamespace(ApplicationAssembly);
        QueryHandlerRules.QueryHandlersShouldNotUseWriteRepositories(ApplicationAssembly);
        DomainRules.EntitiesShouldInheritBaseEntity(Architecture);
        DomainRules.AggregateRootsShouldBeInDomainLayer(Architecture);
        DomainRules.EntitiesShouldHavePrivateSetters(Architecture);
        DomainRules.EntityCreateMethodsShouldBeStatic(Architecture);
        AggregateRootRules.AggregatesShouldInheritFromBaseEntity(Architecture);
        AggregateRootRules.AggregatesShouldResideInNamespace(Architecture, "Domain.Entities");
        DomainEventRules.DomainEventsShouldBeSealed(Architecture);
        DomainEventRules.DomainEventsShouldResideInDomainEventsNamespace(Architecture);
        ValidationRules.ValidatorsShouldBeSealed(Architecture);
    }
}
