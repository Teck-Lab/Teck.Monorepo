using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;
using Teck.Platform.Arch.Tests.Rules;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Pricing.Architecture.UnitTests;

public sealed class PricingArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly Assembly DomainAssembly = typeof(Pricing.Domain.Entities.PriceList).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Pricing.Application.Pricing.Features.ResolvePrice.V1.ResolvePriceHandler).Assembly;
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;

    private static readonly ArchUnitNET.Domain.Architecture PricingArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly)
        .Build();

    [Fact]
    public void PricingHost_ShouldNotReferencePricingDomainDirectly() =>
        Types().That().ResideInAssembly(HostAssembly)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(DomainAssembly))
            .Because("the host must depend on the application layer, not the domain layer directly")
            .Check(PricingArchitecture);

    [Fact]
    public void PricingApplication_ShouldNotReferencePricingHost() =>
        Types().That().ResideInAssembly(ApplicationAssembly)
            .Should().NotDependOnAny(Types().That().ResideInAssembly(HostAssembly))
            .Because("the application layer must not depend on the host layer")
            .Check(PricingArchitecture);

    [Fact]
    public void PricingAggregateRoots_ShouldImplementTenantScoped() =>
        Classes().That().ImplementInterface(typeof(IAggregateRoot))
            .Should().ImplementInterface(typeof(ITenantScoped))
            .Because("tenant-owned pricing aggregates must be scoped to a tenant")
            .Check(PricingArchitecture);

    [Fact]
    public void PricingApplication_ShouldNotDependOnDbContextOrArdalisRepository() =>
        Types().That().ResideInAssembly(ApplicationAssembly)
            .And().DoNotHaveFullNameContaining("DbContext")
            .Should().NotDependOnAny(Types().That().HaveFullNameContaining("DbContext"))
            .AndShould().NotDependOnAny(Types().That().HaveFullNameContaining("Ardalis.Specification.IRepositoryBase"))
            .Because("application handlers must use SharedKernel repository + unit-of-work abstractions")
            .Check(PricingArchitecture);

    [Fact]
    public void PricingEndpoints_ShouldDeriveFromAuthenticatedEndpoint() =>
        Teck.Platform.Arch.Tests.Rules.EndpointRules
            .EndpointsShouldDeriveFromAuthenticatedEndpoint(HostAssembly);

    /// <summary>
    /// Runs every shared architecture rule via <see cref="SharedArchitectureRules.AssertAll"/>.
    /// Unlike basket/customer, pricing HAS <c>IQuery&lt;T&gt;</c> implementors (ResolvePrice,
    /// GetPriceList, ListPriceLists, ListExchangeRates), so <c>QueriesShouldNotModifyState</c> is
    /// included — mirroring the order reference service.
    /// </summary>
    [Fact]
    public void PricingService_ShouldFollowSharedArchitectureRules() =>
        SharedArchitectureRules.AssertAll(PricingArchitecture, ApplicationAssembly);
}
