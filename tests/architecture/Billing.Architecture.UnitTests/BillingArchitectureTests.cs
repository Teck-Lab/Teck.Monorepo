using System.Reflection;
using ArchUnitNET.Loader;
using ArchUnitNET.xUnitV3;
using SharedKernel.Core.Domain;
using Teck.Platform.Arch.Tests.Rules;
using Xunit;
using static ArchUnitNET.Fluent.ArchRuleDefinition;

namespace Billings.Architecture.UnitTests;

public sealed class BillingArchitectureTests : Teck.Platform.Arch.Tests.SharedTestBase
{
    private static readonly Assembly DomainAssembly = typeof(Billings.Domain.Entities.Payment).Assembly;
    private static readonly Assembly ApplicationAssembly = typeof(Billings.Application.Billing.Payments.Features.CapturePayment.V1.CapturePaymentHandler).Assembly;
    private static readonly Assembly HostAssembly = typeof(Program).Assembly;

    private static readonly ArchUnitNET.Domain.Architecture BillingArchitecture = new ArchLoader()
        .LoadAssemblies(DomainAssembly, ApplicationAssembly, HostAssembly)
        .Build();

    [Fact]
    public void BillingHost_ShouldNotReferenceBillingDomainDirectly()
    {
        Types()
            .That()
            .ResideInAssembly(HostAssembly)
            // Infrastructure adapters implement Application ports over the Domain model;
            // endpoint and workflow code must still go through Application contracts.
            .And()
            .DoNotHaveFullNameContaining("Billings.Host.Infrastructure.StripePurchaseGateway")
            .And()
            .DoNotHaveFullNameContaining("Billings.Host.Database.BillingExpiredTenantPurchaseOrders")
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(DomainAssembly))
            .Because("the host must depend on the application layer, not the domain layer directly")
            .Check(BillingArchitecture);
    }

    [Fact]
    public void BillingApplication_ShouldNotReferenceBillingHost()
    {
        Types()
            .That()
            .ResideInAssembly(ApplicationAssembly)
            .Should()
            .NotDependOnAny(Types().That().ResideInAssembly(HostAssembly))
            .Because("the application layer must not depend on the host layer")
            .Check(BillingArchitecture);
    }

    [Fact]
    public void BillingDomainAggregateRoots_ShouldImplementTenantScopedExceptGlobalPurchaseOrder()
    {
        // A purchase precedes tenant activation and lives in Billing's global store. Its Guid
        // TenantId identifies the Customer reservation, not a Finbuckle tenant partition.
        System.Type[] tenantOwnedAggregates = DomainAssembly.GetTypes()
            .Where(type => type is { IsClass: true, IsAbstract: false } &&
                typeof(IAggregateRoot).IsAssignableFrom(type) &&
                type != typeof(Billings.Domain.Entities.TenantPurchaseOrder))
            .ToArray();
        Assert.NotEmpty(tenantOwnedAggregates);
        Assert.All(tenantOwnedAggregates, type => Assert.True(
            typeof(ITenantScoped).IsAssignableFrom(type),
            $"Tenant-owned Billing aggregate '{type.FullName}' must implement ITenantScoped."));
    }

    [Fact]
    public void BillingApplication_ShouldNotDependOnDbContextOrAardalisRepository()
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
            .Check(BillingArchitecture);
    }

    [Fact]
    public void BillingApplicationHandlers_ShouldEndWithHandler()
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
    public void BillingEndpoints_ShouldDeriveFromAuthenticatedEndpoint() =>
        Teck.Platform.Arch.Tests.Rules.EndpointRules
            .EndpointsShouldDeriveFromAuthenticatedEndpoint(HostAssembly);

    [Fact]
    public void BillingService_ShouldFollowSharedArchitectureRules() =>
        SharedArchitectureRules.AssertAll(BillingArchitecture, ApplicationAssembly);
}
