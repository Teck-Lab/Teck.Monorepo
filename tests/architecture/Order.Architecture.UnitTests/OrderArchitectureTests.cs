using System.Reflection;
using ArchUnitNET.Domain;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Orders.Architecture.UnitTests;

public sealed class OrderArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly Assembly DomainAssembly = typeof(Orders.Domain.Entities.Order).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Orders.Application.Orders.Features.CreateOrder.V1.CreateOrderHandler).Assembly;
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;

    private static readonly Architecture OrderArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly)
        .Build();

    [Fact]
    public void OrderHost_ShouldNotReferenceOrderDomainDirectly()
    {
        Types()
            .That()
            .ResideInAssembly(HostAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(DomainAssembly))
            .Because("the host must depend on the application layer, not the domain layer directly")
            .Check(OrderArchitecture);
    }

    [Fact]
    public void OrderApplication_ShouldNotReferenceOrderHost()
    {
        Types()
            .That()
            .ResideInAssembly(ApplicationAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(HostAssembly))
            .Because("the application layer must not depend on the host layer")
            .Check(OrderArchitecture);
    }

    [Fact]
    public void OrderDomainAggregateRoots_ShouldImplementTenantScoped()
    {
        Classes()
            .That()
            .ImplementInterface(typeof(IAggregateRoot))
            .Should()
            .ImplementInterface(typeof(ITenantScoped))
            .Because("tenant-owned order aggregates must be scoped to a tenant")
            .Check(OrderArchitecture);
    }

    [Fact]
    public void OrderApplicationHandlers_ShouldEndWithHandler()
    {
        Type[] handlerTypes = ApplicationAssembly
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
}
