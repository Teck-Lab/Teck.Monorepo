using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;
using Teck.Platform.Arch.Tests.Rules;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Inventories.Architecture.UnitTests;

public sealed class InventoryArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly Assembly DomainAssembly = typeof(Inventories.Domain.Entities.StockItem).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Inventories.Application.Inventory.Features.AdjustStock.V1.AdjustStockHandler).Assembly;
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;

    private static readonly ArchUnitNET.Domain.Architecture InventoryArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly)
        .Build();

    [Fact]
    public void InventoryHost_ShouldNotReferenceInventoryDomainDirectly()
    {
        Types()
            .That()
            .ResideInAssembly(HostAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(DomainAssembly))
            .Because("the host must depend on the application layer, not the domain layer directly")
            .Check(InventoryArchitecture);
    }

    [Fact]
    public void InventoryApplication_ShouldNotReferenceInventoryHost()
    {
        Types()
            .That()
            .ResideInAssembly(ApplicationAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(HostAssembly))
            .Because("the application layer must not depend on the host layer")
            .Check(InventoryArchitecture);
    }

    [Fact]
    public void InventoryDomainAggregateRoots_ShouldImplementTenantScoped()
    {
        Classes()
            .That()
            .ImplementInterface(typeof(IAggregateRoot))
            .Should()
            .ImplementInterface(typeof(ITenantScoped))
            .Because("tenant-owned inventory aggregates must be scoped to a tenant")
            .Check(InventoryArchitecture);
    }

    [Fact]
    public void InventoryApplication_ShouldNotDependOnDbContextOrAardalisRepository()
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
            .Check(InventoryArchitecture);
    }

    [Fact]
    public void InventoryApplicationHandlers_ShouldEndWithHandler()
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
    public void InventoryEndpoints_ShouldDeriveFromAuthenticatedEndpoint() =>
        Teck.Platform.Arch.Tests.Rules.EndpointRules
            .EndpointsShouldDeriveFromAuthenticatedEndpoint(HostAssembly);

    [Fact]
    public void InventoryService_ShouldFollowSharedArchitectureRules() =>
        SharedArchitectureRules.AssertAll(InventoryArchitecture, ApplicationAssembly);
}
