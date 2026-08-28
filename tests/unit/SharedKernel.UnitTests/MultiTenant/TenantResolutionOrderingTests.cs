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
    [Theory]
    [InlineData("tenant-from-selected-signed-claim", "tenant-from-selected-signed-claim")]
    [InlineData("TENANT-FROM-SELECTED-SIGNED-CLAIM", null)]
    public async Task HttpRequest_ResolvesOnlyExactSignedMembershipBeforeDbContextConstruction(
        string headerTenantId,
        string? expectedTenantId)
    {
        const string firstTenantId = "tenant-from-first-signed-claim";
        const string selectedTenantId = "tenant-from-selected-signed-claim";
        await using ServiceProvider provider = CreateServices();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_ids", $"{firstTenantId},{selectedTenantId}")], "test")),
        };
        context.Request.Headers["X-TenantId"] = headerTenantId;

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
            Assert.Equal(expectedTenantId, constructedContext.TenantDetails?.Id);
        }
    }

    [Fact]
    public async Task HttpRequest_FailsClosedWhenHeaderDoesNotSelectSignedMembership()
    {
        const string firstTenantId = "tenant-from-first-signed-claim";
        const string nonMemberTenantId = "tenant-not-in-signed-claims";
        await using ServiceProvider provider = CreateServices();
        await using AsyncServiceScope scope = provider.CreateAsyncScope();
        var context = new DefaultHttpContext
        {
            RequestServices = scope.ServiceProvider,
            User = new ClaimsPrincipal(new ClaimsIdentity([new Claim("tenant_ids", firstTenantId)], "test")),
        };
        context.Request.Headers["X-TenantId"] = nonMemberTenantId;

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
            Assert.Null(constructedContext.TenantDetails);
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
        services.AddSingleton<ITenantTokenContextResolver, TenantTokenContextResolver>();
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
