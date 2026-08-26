using System.Security.Claims;
using Finbuckle.MultiTenant.Abstractions;
using Finbuckle.MultiTenant.AspNetCore;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using NSubstitute;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.Messaging.MultiTenant;
using SharedKernel.Infrastructure.MultiTenant;
using Wolverine;
using Xunit;

namespace SharedKernel.UnitTests.MultiTenant;

/// <summary>Proves that tenant resolution precedes construction of tenant-bound database contexts.</summary>
public sealed class TenantResolutionOrderingTests
{
    [Fact]
    public async Task HttpRequest_ResolvesSignedClaimBeforeDbContextConstruction_AndIgnoresTenantHeader()
    {
        const string claimTenantId = "tenant-from-signed-claim";
        const string hostileHeaderTenantId = "tenant-from-caller-header";
        await using ServiceProvider provider = CreateServices();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_id", claimTenantId)], "test")),
        };
        context.Request.Headers["X-TenantId"] = hostileHeaderTenantId;

        ProbeDbContext? constructedContext = null;
        var middleware = new MultiTenantMiddleware(_ =>
        {
            var accessor = scope.ServiceProvider.GetRequiredService<IMultiTenantContextAccessor<TenantDetails>>();
            constructedContext = new ProbeDbContext(new DbContextOptionsBuilder<ProbeDbContext>().Options, accessor);
            return Task.CompletedTask;
        });

        await middleware.Invoke(context).ConfigureAwait(false);

        Assert.NotNull(constructedContext);
        using (constructedContext)
        {
            Assert.Equal(claimTenantId, constructedContext.TenantDetails?.Id);
        }
    }

    [Fact]
    public void IncomingMessage_ResolvesEnvelopeTenantBeforeDbContextConstruction()
    {
        const string envelopeTenantId = "tenant-from-envelope";
        using ServiceProvider provider = CreateServices();
        using IServiceScope scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IMultiTenantContextAccessor<TenantDetails>>();
        var setter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
        var messageContext = Substitute.For<IMessageContext>();
        messageContext.Envelope.Returns(new Envelope(new TestMessage()) { TenantId = envelopeTenantId });
        var middleware = new TenantPropagationMiddleware(
            accessor,
            setter,
            NullLogger<TenantPropagationMiddleware>.Instance);

        TenantPropagationMiddleware.TenantPropagationScope tenantScope = middleware.Before(messageContext);
        try
        {
            using var constructedContext = new ProbeDbContext(new DbContextOptionsBuilder<ProbeDbContext>().Options, accessor);

            Assert.Equal(envelopeTenantId, constructedContext.TenantDetails?.Id);
        }
        finally
        {
            middleware.Finally(tenantScope);
        }
    }

    [Fact]
    public void IncomingMessage_ResolvedAfterDbContextConstruction_IsReadWhenTheContextExecutes()
    {
        const string envelopeTenantId = "tenant-from-envelope";
        using ServiceProvider provider = CreateServices();
        using IServiceScope scope = provider.CreateScope();
        var accessor = scope.ServiceProvider.GetRequiredService<IMultiTenantContextAccessor<TenantDetails>>();
        var setter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
        var messageContext = Substitute.For<IMessageContext>();
        messageContext.Envelope.Returns(new Envelope(new TestMessage()) { TenantId = envelopeTenantId });
        var middleware = new TenantPropagationMiddleware(
            accessor,
            setter,
            NullLogger<TenantPropagationMiddleware>.Instance);
        using var constructedContext = new ProbeDbContext(new DbContextOptionsBuilder<ProbeDbContext>().Options, accessor);

        Assert.Null(constructedContext.TenantDetails);

        TenantPropagationMiddleware.TenantPropagationScope tenantScope = middleware.Before(messageContext);
        try
        {
            Assert.Equal(envelopeTenantId, constructedContext.TenantDetails?.Id);
        }
        finally
        {
            middleware.Finally(tenantScope);
        }
    }

    [Fact]
    public void IncomingMessage_ResolvedAfterTenantInfoInjection_UsesEnvelopeTenantWhenTheHandlerRuns()
    {
        const string envelopeTenantId = "tenant-from-envelope";
        using ServiceProvider provider = CreateServices(includeApplicationTenantInfo: true);
        using IServiceScope scope = provider.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantInfo>();
        var accessor = scope.ServiceProvider.GetRequiredService<IMultiTenantContextAccessor<TenantDetails>>();
        var setter = scope.ServiceProvider.GetRequiredService<IMultiTenantContextSetter>();
        var messageContext = Substitute.For<IMessageContext>();
        messageContext.Envelope.Returns(new Envelope(new TestMessage()) { TenantId = envelopeTenantId });
        var middleware = new TenantPropagationMiddleware(
            accessor,
            setter,
            NullLogger<TenantPropagationMiddleware>.Instance);

        Assert.Null(accessor.MultiTenantContext?.TenantInfo);

        TenantPropagationMiddleware.TenantPropagationScope tenantScope = middleware.Before(messageContext);
        try
        {
            Assert.Equal(envelopeTenantId, tenant.Id);
        }
        finally
        {
            middleware.Finally(tenantScope);
        }
    }

    [Fact]
    public void TenantInfo_WithoutAnAmbientTenant_FailsWhenUsed()
    {
        using ServiceProvider provider = CreateServices(includeApplicationTenantInfo: true);
        using IServiceScope scope = provider.CreateScope();
        var tenant = scope.ServiceProvider.GetRequiredService<ITenantInfo>();

        InvalidOperationException exception = Assert.Throws<InvalidOperationException>(() => _ = tenant.Id);

        Assert.Contains("No tenant is active", exception.Message, StringComparison.Ordinal);
    }

    private static ServiceProvider CreateServices(bool includeApplicationTenantInfo = false)
    {
        var services = new ServiceCollection();
        services.AddTeckCloudMultiTenancy();
        if (includeApplicationTenantInfo)
        {
            services.AddScoped<ITenantInfo, AmbientTenantInfo>();
        }

        return services.BuildServiceProvider();
    }

    private sealed class ProbeDbContext(
        DbContextOptions options,
        IMultiTenantContextAccessor<TenantDetails> accessor)
        : BaseDbContext(options, tenantAccessor: accessor);

    private sealed record TestMessage;
}
