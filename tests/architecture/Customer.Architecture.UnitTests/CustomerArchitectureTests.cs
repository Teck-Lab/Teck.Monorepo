using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using Teck.Platform.Arch.Tests.Rules;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Customers.Architecture.UnitTests;

/// <summary>
/// Architecture tests for the Customer service.
///
/// Rules skipped vs. the Order/Catalog baseline and rationale:
/// <list type="bullet">
///   <item>
///     <term>CustomerHost_ShouldNotReferenceCustomerDomainDirectly</term>
///     <description>
///       <see cref="Customers.Host.Grpc.V1.GetTenantDatabaseInfoCommandHandler"/> depends on
///       <see cref="Customers.Domain.Entities.Tenant"/> via the
///       <c>IGenericReadRepository&lt;Tenant, Guid&gt;</c> generic parameter. This is intentional:
///       the customer host is the tenant authority and the handler lives in Host by design
///       (FastEndpoints <c>ICommandHandler</c>, not a WolverineFx Application handler).
///     </description>
///   </item>
///   <item>
///     <term>CustomerDomainAggregateRoots_ShouldImplementTenantScoped</term>
///     <description>
///       <see cref="Customers.Domain.Entities.Tenant"/> is the global tenant registry and is
///       explicitly NOT tenant-scoped — it IS the source of truth for every other service's
///       tenant context. The <c>ITenantScoped</c> rule does not apply here.
///     </description>
///   </item>
///   <item>
///     <term>CustomerApplicationHandlers_ShouldEndWithHandler</term>
///     <description>
///       Customer.Application contains no WolverineFx static handlers. The only handler is
///       the FastEndpoints <c>GetTenantDatabaseInfoCommandHandler</c> in Customer.Host.
///     </description>
///   </item>
///   <item>
///     <term>CustomerEndpoints_ShouldDeriveFromAuthenticatedEndpoint</term>
///     <description>
///       Customer.Host exposes no HTTP endpoints (only gRPC remote handler traffic).
///       The rule asserts <c>NotEmpty</c> and would fail with zero endpoints.
///     </description>
///   </item>
///   <item>
///     <term>CommandsShouldBeImmutable / QueriesShouldNotModifyState (inside AssertAll)</term>
///     <description>
///       ArchUnitNET 0.13.3 crashes (NullReferenceException in HashSet construction) when
///       <c>AreAssignableTo(typeof(ICommand&lt;&gt;))</c> or <c>AreAssignableTo(typeof(IQuery&lt;&gt;))</c>
///       is evaluated against an architecture that has ZERO implementations of those interfaces.
///       The bug is triggered by covariant open generics with empty implementor sets when the
///       declaring assembly (SharedKernel.Core) is not in the architecture. Customer.Application
///       deliberately has no WolverineFx CQRS commands or queries — these rules are enforced
///       by the reflection-based handler checks (which pass with zero violations) and would
///       pass trivially here anyway. <see cref="CustomerService_ShouldFollowSharedArchitectureRules"/>
///       calls all other rules from <see cref="SharedArchitectureRules.AssertAll"/> directly.
///     </description>
///   </item>
/// </list>
/// </summary>
public sealed class CustomerArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly System.Reflection.Assembly DomainAssembly =
        typeof(Customers.Domain.Entities.Tenant).Assembly;

    private static readonly System.Reflection.Assembly ApplicationAssembly =
        typeof(Customers.Application.Tenants.ReadModels.TenantByIdSpec).Assembly;

    private static readonly System.Reflection.Assembly HostAssembly = typeof(Program).Assembly;

    // Full architecture (all three layers) — used for layer-dependency rules.
    private static readonly ArchUnitNET.Domain.Architecture CustomerArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly)
        .Build();

    // Domain + Application only — used for domain model rules in
    // CustomerService_ShouldFollowSharedArchitectureRules. The Host assembly is excluded
    // because FastEndpoints.Messaging.Remote types loaded transitively from Customer.Host
    // cause ArchUnitNET crashes in AreAssignableTo checks (see class-level doc above).
    private static readonly ArchUnitNET.Domain.Architecture DomainAndApplicationArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly)
        .Build();

    /// <summary>The application layer must not take a dependency on the host layer.</summary>
    [Fact]
    public void CustomerApplication_ShouldNotReferenceCustomerHost()
    {
        Types()
            .That()
            .ResideInAssembly(ApplicationAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(HostAssembly))
            .Because("the application layer must not depend on the host layer")
            .Check(CustomerArchitecture);
    }

    /// <summary>
    /// Application types (other than the DbContext base classes) must not depend on a concrete
    /// DbContext or on Ardalis <c>IRepositoryBase</c>.
    /// </summary>
    [Fact]
    public void CustomerApplication_ShouldNotDependOnDbContextOrAardalisRepository()
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
            .Because("application handlers must use SharedKernel repository + unit-of-work abstractions")
            .Check(CustomerArchitecture);
    }

    /// <summary>
    /// Runs all applicable shared architecture rules (CQRS handler naming/placement, domain model
    /// conventions, events, validators). The two rules that check immutability of <c>ICommand&lt;&gt;</c>
    /// and <c>IQuery&lt;&gt;</c> implementations are omitted because ArchUnitNET 0.13.3 crashes when
    /// those interfaces have zero implementors in the architecture (see class-level doc). Customer has
    /// no WolverineFx CQRS commands or queries, so both rules would pass trivially.
    /// </summary>
    [Fact]
    public void CustomerService_ShouldFollowSharedArchitectureRules()
    {
        // Reflection-based handler rules (all pass with zero violations — no WolverineFx handlers).
        CommandHandlerRules.CommandHandlersShouldBeStaticClassesEndingWithHandler(ApplicationAssembly);
        CommandHandlerRules.CommandHandlersShouldResideInFeaturesNamespace(ApplicationAssembly);
        CommandHandlerRules.CommandHandlersShouldNotUseReadRepositories(ApplicationAssembly);

        // CommandHandlerRules.CommandsShouldBeImmutable(DomainAndApplicationArchitecture) — SKIPPED
        // See class-level doc: ArchUnitNET 0.13.3 bug with empty covariant-generic implementor sets.

        QueryHandlerRules.QueryHandlersShouldBeStaticClassesEndingWithHandler(ApplicationAssembly);
        QueryHandlerRules.QueryHandlersShouldResideInFeaturesNamespace(ApplicationAssembly);
        QueryHandlerRules.QueryHandlersShouldNotUseWriteRepositories(ApplicationAssembly);

        // QueryHandlerRules.QueriesShouldNotModifyState(DomainAndApplicationArchitecture) — SKIPPED
        // Same ArchUnitNET bug as above for IQuery<>.

        // Domain model rules (all pass for Customer).
        DomainRules.EntitiesShouldInheritBaseEntity(DomainAndApplicationArchitecture);
        DomainRules.AggregateRootsShouldBeInDomainLayer(DomainAndApplicationArchitecture);
        DomainRules.EntitiesShouldHavePrivateSetters(DomainAndApplicationArchitecture);
        DomainRules.EntityCreateMethodsShouldBeStatic(DomainAndApplicationArchitecture);

        AggregateRootRules.AggregatesShouldInheritFromBaseEntity(DomainAndApplicationArchitecture);
        AggregateRootRules.AggregatesShouldResideInNamespace(DomainAndApplicationArchitecture, "Domain.Entities");

        // Event/validator rules (pass trivially — Customer has no domain events or validators yet).
        DomainEventRules.DomainEventsShouldBeSealed(DomainAndApplicationArchitecture);
        DomainEventRules.DomainEventsShouldResideInDomainEventsNamespace(DomainAndApplicationArchitecture);
        ValidationRules.ValidatorsShouldBeSealed(DomainAndApplicationArchitecture);
    }
}
