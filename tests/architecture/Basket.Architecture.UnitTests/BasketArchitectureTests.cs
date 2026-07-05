using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;
using Teck.Platform.Arch.Tests.Rules;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Baskets.Architecture.UnitTests;

public sealed class BasketArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly Assembly DomainAssembly = typeof(Baskets.Domain.Entities.Basket).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Baskets.Application.Baskets.Features.Checkout.V1.CheckoutHandler).Assembly;
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;

    private static readonly ArchUnitNET.Domain.Architecture BasketArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly)
        .Build();

    [Fact]
    public void BasketHost_ShouldNotReferenceBasketDomainDirectly()
    {
        Types()
            .That()
            .ResideInAssembly(HostAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(DomainAssembly))
            .Because("the host must depend on the application layer, not the domain layer directly")
            .Check(BasketArchitecture);
    }

    [Fact]
    public void BasketApplication_ShouldNotReferenceBasketHost()
    {
        Types()
            .That()
            .ResideInAssembly(ApplicationAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(HostAssembly))
            .Because("the application layer must not depend on the host layer")
            .Check(BasketArchitecture);
    }

    [Fact]
    public void BasketDomainAggregateRoots_ShouldImplementTenantScoped()
    {
        Classes()
            .That()
            .ImplementInterface(typeof(IAggregateRoot))
            .Should()
            .ImplementInterface(typeof(ITenantScoped))
            .Because("tenant-owned basket aggregates must be scoped to a tenant")
            .Check(BasketArchitecture);
    }

    [Fact]
    public void BasketApplication_ShouldNotDependOnDbContextOrAardalisRepository()
    {
        Types()
            .That()
            .ResideInAssembly(ApplicationAssembly)
            .And()
            .DoNotHaveFullNameContaining("DbContext")
            .Should()
            .NotDependOnAny(Types().That().HaveFullNameContaining("DbContext"))
            .AndShould()
            .NotDependOnAny(Types().That().HaveFullNameContaining("Ardalis.Specification.IRepositoryBase"))
            .Because("application handlers must use SharedKernel repository + unit-of-work abstractions, not a concrete DbContext or Ardalis IRepositoryBase")
            .Check(BasketArchitecture);
    }

    [Fact]
    public void BasketApplicationHandlers_ShouldEndWithHandler()
    {
        System.Type[] handlerTypes = ApplicationAssembly
            .GetTypes()
            .Where(type =>
                type.IsClass
                && type.IsAbstract
                && type.IsSealed
                && type.GetMethods(BindingFlags.Public | BindingFlags.Static).Any(method => method.Name == "Handle"))
            .ToArray();

        Assert.NotEmpty(handlerTypes);
        Assert.All(handlerTypes, handlerType => Assert.EndsWith("Handler", handlerType.Name, StringComparison.Ordinal));
    }

    [Fact]
    public void BasketEndpoints_ShouldDeriveFromAuthenticatedEndpoint() =>
        Teck.Platform.Arch.Tests.Rules.EndpointRules
            .EndpointsShouldDeriveFromAuthenticatedEndpoint(HostAssembly);

    /// <summary>
    /// Runs every shared architecture rule directly rather than via
    /// <see cref="SharedArchitectureRules.AssertAll"/>, because
    /// <see cref="QueryHandlerRules.QueriesShouldNotModifyState"/> is deliberately omitted.
    /// Basket.Application models every feature (including reads such as get-or-create) as
    /// <c>ICommand&lt;T&gt;</c> and has zero <c>IQuery&lt;T&gt;</c> types, so ArchUnitNET throws
    /// <c>TypeDoesNotExistInArchitecture</c> when <c>AreAssignableTo(typeof(IQuery&lt;&gt;))</c> is
    /// evaluated against an architecture with no <c>IQuery&lt;&gt;</c> implementors and where the
    /// declaring assembly (SharedKernel.Core) is not loaded — the same behaviour documented on
    /// <c>Customers.Architecture.UnitTests.CustomerArchitectureTests</c>. The reflection-based
    /// query-handler rules below still enforce the read-side conventions, and
    /// <c>CommandsShouldBeImmutable</c> is retained because Basket does have <c>ICommand&lt;T&gt;</c>
    /// implementors, so that interface loads and the rule passes cleanly.
    /// </summary>
    [Fact]
    public void BasketService_ShouldFollowSharedArchitectureRules()
    {
        // CQRS command handlers (WolverineFx static Handle methods).
        CommandHandlerRules.CommandHandlersShouldBeStaticClassesEndingWithHandler(ApplicationAssembly);
        CommandHandlerRules.CommandHandlersShouldResideInFeaturesNamespace(ApplicationAssembly);
        CommandHandlerRules.CommandHandlersShouldNotUseReadRepositories(ApplicationAssembly);
        CommandHandlerRules.CommandsShouldBeImmutable(BasketArchitecture);

        // CQRS query handlers — reflection-based rules only.
        QueryHandlerRules.QueryHandlersShouldBeStaticClassesEndingWithHandler(ApplicationAssembly);
        QueryHandlerRules.QueryHandlersShouldResideInFeaturesNamespace(ApplicationAssembly);
        QueryHandlerRules.QueryHandlersShouldNotUseWriteRepositories(ApplicationAssembly);

        // QueryHandlerRules.QueriesShouldNotModifyState(BasketArchitecture) — SKIPPED.
        // Basket has zero IQuery<> types; ArchUnitNET throws on AreAssignableTo(IQuery<>) against an
        // empty implementor set (see method doc). The read-side is still covered by the rules above.

        // Domain model.
        DomainRules.EntitiesShouldInheritBaseEntity(BasketArchitecture);
        DomainRules.AggregateRootsShouldBeInDomainLayer(BasketArchitecture);
        DomainRules.EntitiesShouldHavePrivateSetters(BasketArchitecture);
        DomainRules.EntityCreateMethodsShouldBeStatic(BasketArchitecture);

        AggregateRootRules.AggregatesShouldInheritFromBaseEntity(BasketArchitecture);
        AggregateRootRules.AggregatesShouldResideInNamespace(BasketArchitecture, "Domain.Entities");

        // Domain events / validators.
        DomainEventRules.DomainEventsShouldBeSealed(BasketArchitecture);
        DomainEventRules.DomainEventsShouldResideInDomainEventsNamespace(BasketArchitecture);
        ValidationRules.ValidatorsShouldBeSealed(BasketArchitecture);
    }
}
