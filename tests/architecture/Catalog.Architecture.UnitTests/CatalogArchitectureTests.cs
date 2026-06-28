using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;
using Teck.Platform.Arch.Tests.Rules;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Catalog.Architecture.UnitTests;

public sealed class CatalogArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly Assembly DomainAssembly = typeof(Catalog.Domain.Entities.Product).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Catalog.Application.Products.Features.CreateProduct.V1.CreateProductHandler).Assembly;
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;

    private static readonly ArchUnitNET.Domain.Architecture CatalogArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly)
        .Build();

    [Fact]
    public void CatalogHost_ShouldNotReferenceCatalogDomainDirectly()
    {
        Types()
            .That()
            .ResideInAssembly(HostAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(DomainAssembly))
            .Because("the host must depend on the application layer, not the domain layer directly")
            .Check(CatalogArchitecture);
    }

    [Fact]
    public void CatalogApplication_ShouldNotReferenceCatalogHost()
    {
        Types()
            .That()
            .ResideInAssembly(ApplicationAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(HostAssembly))
            .Because("the application layer must not depend on the host layer")
            .Check(CatalogArchitecture);
    }

    [Fact]
    public void CatalogDomainAggregateRoots_ShouldImplementTenantScoped()
    {
        Classes()
            .That()
            .ImplementInterface(typeof(IAggregateRoot))
            .Should()
            .ImplementInterface(typeof(ITenantScoped))
            .Because("tenant-owned catalog aggregates must be scoped to a tenant")
            .Check(CatalogArchitecture);
    }

    [Fact]
    public void CatalogApplication_ShouldNotDependOnDbContextOrAardalisRepository()
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
            .Check(CatalogArchitecture);
    }

    [Fact]
    public void CatalogApplicationHandlers_ShouldEndWithHandler()
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
    public void CatalogService_ShouldFollowSharedArchitectureRules() =>
        SharedArchitectureRules.AssertAll(CatalogArchitecture, ApplicationAssembly);
}
