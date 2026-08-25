using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Options;
using NSubstitute;
using SharedKernel.Infrastructure.Middlewares;
using SharedKernel.Infrastructure.MultiTenant;
using Wolverine;
using Xunit;

namespace SharedKernel.UnitTests.Middlewares;

public sealed class TenantMessageBusMiddlewareTests
{
    [Fact]
    public async Task InvokeAsync_SingleSignedTenant_ScopesBusAndRestoresPreviousTenant()
    {
        const string signedTenant = "signed-tenant";
        var tenant = new TenantHolder { Value = "previous-tenant" };
        IMessageBus bus = CreateBus(tenant);
        var resolver = new StubTenantTokenContextResolver(signedTenant);
        string? tenantObservedByNext = null;
        var middleware = new TenantMessageBusMiddleware(_ =>
        {
            tenantObservedByNext = tenant.Value;
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(AuthenticatedContext(), bus, resolver, Options.Create(new TeckCloudMultiTenancyOptions()));

        Assert.Equal(signedTenant, tenantObservedByNext);
        Assert.Equal("previous-tenant", tenant.Value);
    }

    [Theory]
    [InlineData(false, "signed-tenant")]
    [InlineData(true)]
    [InlineData(true, "tenant-a", "tenant-b")]
    public async Task InvokeAsync_AnonymousOrAmbiguousClaims_LeavesBusTenantUnset(bool authenticated, params string[] tenantIds)
    {
        var tenant = new TenantHolder();
        IMessageBus bus = CreateBus(tenant);
        var middleware = new TenantMessageBusMiddleware(_ => Task.CompletedTask);
        DefaultHttpContext context = authenticated ? AuthenticatedContext() : new DefaultHttpContext();

        await middleware.InvokeAsync(
            context,
            bus,
            new StubTenantTokenContextResolver(tenantIds),
            Options.Create(new TeckCloudMultiTenancyOptions()));

        Assert.Null(tenant.Value);
    }

    private static IMessageBus CreateBus(TenantHolder tenant)
    {
        IMessageBus bus = Substitute.For<IMessageBus>();
        bus.TenantId.Returns(_ => tenant.Value);
        bus.When(candidate => candidate.TenantId = Arg.Any<string?>())
            .Do(call => tenant.Value = call.Arg<string?>());
        return bus;
    }

    private static DefaultHttpContext AuthenticatedContext()
    {
        var context = new DefaultHttpContext();
        context.User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("sub", "test-subject")], "test"));
        return context;
    }

    private sealed class StubTenantTokenContextResolver(params string[] tenantIds) : ITenantTokenContextResolver
    {
        public IReadOnlyList<string> ResolveTenantIds(
            ClaimsPrincipal user,
            string organizationClaimName,
            string tenantIdClaimName) => tenantIds;
    }

    private sealed class TenantHolder
    {
        public string? Value { get; set; }
    }
}
