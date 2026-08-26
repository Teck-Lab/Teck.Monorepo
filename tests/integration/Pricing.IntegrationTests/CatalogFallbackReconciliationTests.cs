// <copyright file="CatalogFallbackReconciliationTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Options;
using Pricing.Application.Database;
using Pricing.Application.Pricing;
using Pricing.Application.Pricing.EventHandlers.IntegrationEvents;
using Pricing.Application.Pricing.Features.ResolvePrice.V1;
using Pricing.Domain.Entities;
using Pricing.Host.Database;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Infrastructure.MultiTenant;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Xunit;

namespace Pricing.IntegrationTests;

/// <summary>Exercises the bounded asynchronous catalog fallback reconciliation path against PostgreSQL.</summary>
[Collection("SharedTestcontainers")]
public sealed class CatalogFallbackReconciliationTests : PricingIntegrationTestBase
{
    private const string TenantA = "tenant-a";
    private const string TenantB = "tenant-b";

    /// <summary>Initializes the test against the shared pricing host fixture.</summary>
    public CatalogFallbackReconciliationTests(SharedTestcontainersFixture fixture)
        : base(fixture)
    {
    }

    /// <summary>Resolves a catalog product that existed before pricing subscribed through request and response contracts.</summary>
    [Fact]
    public async Task Reconciliation_ProductPredatesSubscription_ResolvesAtCatalogSellPrice()
    {
        Guid productId = Guid.NewGuid();
        BasketCheckoutRequestedIntegrationEvent request = CheckoutRequest(productId, TenantA, "pre-subscription-request");

        await StartReconciliationAsync(request);
        await CompleteReconciliationAsync(productId, TenantA, request.RequestId, 12.50m);

        await using PricingReadDbContext read = CreateReadContext(TenantA);
        var resolved = await ResolvePriceHandler.ResolveAsync(
            new ResolvePriceQuery(productId, "USD", 2, null, null, null, DateTimeOffset.UtcNow),
            new PricingReadRepository<Price, Guid>(read),
            new PricingReadRepository<ExchangeRate, Guid>(read),
            new PricingReadRepository<CatalogPrice, Guid>(read),
            Options.Create(new PricingOptions()),
            CancellationToken.None);

        Assert.False(resolved.IsError);
        Assert.Equal(12.50m, resolved.Value.UnitAmount);
        Assert.Equal("USD", resolved.Value.Currency);
        Assert.True(await IsResolvedAsync(TenantA, request.RequestId));
    }

    /// <summary>Recovers a catalog price change missed before the pricing subscriber started without replaying an event stream.</summary>
    [Fact]
    public async Task Reconciliation_PriceChangeBeforeSubscriberStarted_UsesCurrentCatalogSellPrice()
    {
        Guid productId = Guid.NewGuid();
        BasketCheckoutRequestedIntegrationEvent request = CheckoutRequest(productId, TenantA, "missed-price-change-request");

        await StartReconciliationAsync(request);
        await CompleteReconciliationAsync(productId, TenantA, request.RequestId, 23.75m);

        await using PricingReadDbContext read = CreateReadContext(TenantA);
        var fallback = await new PricingReadRepository<CatalogPrice, Guid>(read)
            .FirstOrDefaultAsync(new Pricing.Application.Pricing.ReadModels.CatalogPriceByProductSpec(productId, TenantA));

        Assert.NotNull(fallback);
        Assert.Equal(23.75m, fallback!.Amount);
        Assert.True(await IsResolvedAsync(TenantA, request.RequestId));
    }

    /// <summary>Scopes fallback projections by tenant and absorbs response redelivery without duplicating rows or resumptions.</summary>
    [Fact]
    public async Task Reconciliation_IsTenantScopedAndIdempotentUnderRedelivery()
    {
        Guid productId = Guid.NewGuid();
        BasketCheckoutRequestedIntegrationEvent tenantARequest = CheckoutRequest(productId, TenantA, "tenant-a-request");
        BasketCheckoutRequestedIntegrationEvent tenantBRequest = CheckoutRequest(productId, TenantB, "tenant-b-request");

        await StartReconciliationAsync(tenantARequest);
        await StartReconciliationAsync(tenantBRequest);
        await CompleteReconciliationAsync(productId, TenantA, tenantARequest.RequestId, 10m);
        await CompleteReconciliationAsync(productId, TenantA, tenantARequest.RequestId, 10m);
        await CompleteReconciliationAsync(productId, TenantB, tenantBRequest.RequestId, 20m);

        await using PricingDbContext tenantA = CreateWriteContext(TenantA);
        await using PricingDbContext tenantB = CreateWriteContext(TenantB);
        Assert.Equal(10m, Assert.Single(await tenantA.CatalogPrices.Where(price => price.TenantId == TenantA).ToListAsync()).Amount);
        Assert.Equal(20m, Assert.Single(await tenantB.CatalogPrices.Where(price => price.TenantId == TenantB).ToListAsync()).Amount);
        Assert.Single(await tenantA.PendingPriceResolutions.Where(pending => pending.TenantId == TenantA).ToListAsync());
        Assert.Single(await tenantB.PendingPriceResolutions.Where(pending => pending.TenantId == TenantB).ToListAsync());
        Assert.True(await IsResolvedAsync(TenantA, tenantARequest.RequestId));
        Assert.True(await IsResolvedAsync(TenantB, tenantBRequest.RequestId));
    }

    private async Task StartReconciliationAsync(BasketCheckoutRequestedIntegrationEvent request)
    {
        using IServiceScope scope = Services.CreateScope();
        await using PricingDbContext write = CreateWriteContext(request.TenantId);
        await using PricingReadDbContext read = CreateReadContext(request.TenantId);
        using var unitOfWork = new UnitOfWork<PricingDbContext>(write);
        await BasketCheckoutRequestedHandler.Handle(
            request,
            new PricingReadRepository<Price, Guid>(read),
            new PricingReadRepository<ExchangeRate, Guid>(read),
            new PricingReadRepository<CatalogPrice, Guid>(read),
            new PricingWriteRepository<PendingPriceResolution, Guid>(write, new HttpContextAccessor()),
            unitOfWork,
            Options.Create(new PricingOptions()),
            scope.ServiceProvider.GetRequiredService<IMessageBus>(),
            CancellationToken.None);
    }

    private async Task CompleteReconciliationAsync(Guid productId, string tenantId, string requestId, decimal amount)
    {
        using IServiceScope scope = Services.CreateScope();
        await using PricingDbContext write = CreateWriteContext(tenantId);
        await using PricingReadDbContext read = CreateReadContext(tenantId);
        using var unitOfWork = new UnitOfWork<PricingDbContext>(write);
        await CatalogPriceReconciledHandler.Handle(
            new CatalogPriceReconciledIntegrationEvent
            {
                ProductId = productId,
                VariantId = Guid.NewGuid(),
                TenantId = tenantId,
                RequestId = requestId,
                SourceCorrelationId = requestId,
                Amount = amount,
                Currency = "USD",
            },
            new PricingWriteRepository<CatalogPrice, Guid>(write, new HttpContextAccessor()),
            new PricingWriteRepository<PendingPriceResolution, Guid>(write, new HttpContextAccessor()),
            new PricingReadRepository<Price, Guid>(read),
            new PricingReadRepository<ExchangeRate, Guid>(read),
            new PricingReadRepository<CatalogPrice, Guid>(read),
            unitOfWork,
            Options.Create(new PricingOptions()),
            scope.ServiceProvider.GetRequiredService<IMessageBus>(),
            CancellationToken.None);
    }

    private async Task<bool> IsResolvedAsync(string tenantId, string requestId)
    {
        await using PricingDbContext context = CreateWriteContext(tenantId);
        return Assert.Single(await context.PendingPriceResolutions.Where(pending => pending.RequestId == requestId).ToListAsync()).IsResolved;
    }

    private PricingDbContext CreateWriteContext(string tenantId) => new(
        new DbContextOptionsBuilder<PricingDbContext>()
            .UseNpgsql(DatabaseConnectionString)
            .UseTeckCloudTenant(tenantId)
            .Options,
        null!);

    private PricingReadDbContext CreateReadContext(string tenantId) => new(
        new DbContextOptionsBuilder<PricingReadDbContext>()
            .UseNpgsql(DatabaseConnectionString)
            .UseTeckCloudTenant(tenantId)
            .Options,
        null!);

    private static BasketCheckoutRequestedIntegrationEvent CheckoutRequest(Guid productId, string tenantId, string requestId) => new()
    {
        BasketId = Guid.NewGuid(),
        TenantId = tenantId,
        AuthorizedAmount = 100m,
        Currency = "USD",
        RequestId = requestId,
        SourceCorrelationId = requestId,
        Lines = [new BasketCheckoutRequestedLine { ProductId = productId, Quantity = 2 }],
    };
}
