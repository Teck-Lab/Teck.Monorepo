using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;
using Teck.Platform.Arch.Tests.Rules;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Customers.Architecture.UnitTests;

/// <summary>
/// Architecture tests for the Customer service.
///
/// Customer is now a completed service: it has a tenant-scoped <c>Customer</c> aggregate,
/// WolverineFx CQRS handlers, and FastEndpoints HTTP endpoints, so it follows the same rule set
/// as its Catalog/Basket siblings. Two carve-outs remain, both genuinely specific to Customer's
/// role as the platform's tenant authority:
/// <list type="bullet">
///   <item>
///     <term>CustomerHost_ShouldNotReferenceCustomerDomainDirectly</term>
///     <description>
///       <see cref="Customers.Host.Grpc.V1.GetTenantDatabaseInfoCommandHandler"/> depends on
///       <see cref="Customers.Domain.Entities.Tenant"/> via the
///       <c>IGenericReadRepository&lt;Tenant, Guid&gt;</c> generic parameter. This is intentional:
///       the customer host is the tenant authority and the handler lives in Host by design
///       (FastEndpoints <c>ICommandHandler</c> serving gRPC, not a WolverineFx Application handler).
///       This rule is therefore not enforced for Customer.
///     </description>
///   </item>
///   <item>
///     <term>Tenant-scoped aggregate roots (except <see cref="Customers.Domain.Entities.Tenant"/>)</term>
///     <description>
///       <see cref="Customers.Domain.Entities.Tenant"/> is the global tenant registry — it IS the
///       source of truth for every other service's tenant context, so it cannot itself be scoped to
///       a tenant. <see cref="Customers.Domain.Entities.Customer"/> is a normal tenant-owned
///       aggregate and must implement <c>ITenantScoped</c> like any other service's aggregates.
///       <see cref="SharedArchitectureRules"/> has no built-in exclusion hook for this, so
///       <see cref="CustomerDomainAggregateRoots_ShouldImplementTenantScopedExceptTenant"/>
///       hand-rolls the check via reflection.
///     </description>
///   </item>
/// </list>
/// </summary>
public sealed class CustomerArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly Assembly DomainAssembly = typeof(Customers.Domain.Entities.Tenant).Assembly;

    private static readonly Assembly ApplicationAssembly =
        typeof(Customers.Application.Customers.Features.CreateCustomer.V1.CreateCustomerHandler).Assembly;

    private static readonly Assembly HostAssembly = typeof(Program).Assembly;

    // Full architecture (all three layers) — used for layer-dependency rules.
    private static readonly ArchUnitNET.Domain.Architecture CustomerArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly)
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
    /// Every domain aggregate root must implement <c>ITenantScoped</c>, except
    /// <see cref="Customers.Domain.Entities.Tenant"/> — the global tenant registry, which is
    /// explicitly NOT tenant-scoped because it IS the source of tenant context for every other
    /// service. <see cref="Customers.Domain.Entities.Customer"/> (and any future Customer
    /// aggregate) must satisfy the rule like any other service's tenant-owned aggregates.
    /// </summary>
    [Fact]
    public void CustomerDomainAggregateRoots_ShouldImplementTenantScopedExceptTenant()
    {
        System.Type[] aggregateRootTypes = DomainAssembly
            .GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false }
                && typeof(IAggregateRoot).IsAssignableFrom(type)
                && type != typeof(Customers.Domain.Entities.Tenant))
            .ToArray();

        Assert.NotEmpty(aggregateRootTypes);
        Assert.Contains(typeof(Customers.Domain.Entities.Customer), aggregateRootTypes);

        Assert.All(
            aggregateRootTypes,
            type => Assert.True(
                typeof(ITenantScoped).IsAssignableFrom(type),
                $"Aggregate root '{type.FullName}' must implement ITenantScoped."));
    }

    /// <summary>
    /// Every concrete FastEndpoints endpoint in Customer.Host must derive from
    /// <see cref="SharedKernel.Infrastructure.Endpoints.AuthenticatedEndpoint{TRequest,TResponse}"/>.
    /// </summary>
    [Fact]
    public void CustomerEndpoints_ShouldDeriveFromAuthenticatedEndpoint() =>
        EndpointRules.EndpointsShouldDeriveFromAuthenticatedEndpoint(HostAssembly);

    /// <summary>WolverineFx application handlers must be static classes named '...Handler'.</summary>
    [Fact]
    public void CustomerApplicationHandlers_ShouldEndWithHandler()
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

    /// <summary>
    /// Runs every rule in <see cref="SharedArchitectureRules.AssertAll"/>, including
    /// <see cref="CommandHandlerRules.CommandsShouldBeImmutable"/> and
    /// <see cref="QueryHandlerRules.QueriesShouldNotModifyState"/>. Customer.Application now has
    /// real <c>ICommand&lt;T&gt;</c> and <c>IQuery&lt;T&gt;</c> implementors, so the ArchUnitNET
    /// 0.13.3 empty-implementor-set crash previously documented on this rule no longer applies —
    /// verified passing against the full Domain+Application+Host architecture.
    /// </summary>
    [Fact]
    public void CustomerService_ShouldFollowSharedArchitectureRules() =>
        SharedArchitectureRules.AssertAll(CustomerArchitecture, ApplicationAssembly);
}
