// <copyright file="ProjectionBootstrapTests.cs" company="TeckLab">
// Copyright (c) TeckLab. All rights reserved.
// </copyright>

using Customers.Application.Database;
using Customers.Application.Customers.EventHandlers.IntegrationEvents;
using Customers.Domain.Entities;
using Customers.Host.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using NSubstitute;
using Pricing.Application.Database;
using Pricing.Application.Pricing;
using Pricing.Application.Pricing.EventHandlers.IntegrationEvents;
using Pricing.Application.Pricing.Features.ResolvePrice.V1;
using Pricing.Domain.Entities;
using Pricing.Host.Database;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Wolverine;
using Xunit;

namespace CrossService.IntegrationTests;

/// <summary>Proves the bounded reconciliation events bootstrap projections that predate subscriptions.</summary>
[Collection("SharedTestcontainers")]
public sealed class ProjectionBootstrapTests(SharedTestcontainersFixture fixture)
{
    /// <summary>Reconciles a catalog sell price through the persisted pricing projection and resumes checkout once.</summary>
    [Fact]
    public async Task CatalogPriceBeforeSubscription_ReconcilesAndResolvesForTheOriginalTenant()
    {
        const string tenantId = "bootstrap-catalog-tenant";
        string connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(PricingDbContext), "Pricing.Host");
        Guid productId = Guid.NewGuid();
        const string requestId = "catalog-before-subscription";
        var request = new BasketCheckoutRequestedIntegrationEvent
        {
            BasketId = Guid.NewGuid(), TenantId = tenantId, AuthorizedAmount = 30m, Currency = "USD",
            RequestId = requestId, SourceCorrelationId = requestId,
            Lines = [new BasketCheckoutRequestedLine { ProductId = productId, Quantity = 2 }],
        };

        await using (var write = CreatePricingWriteContext(connectionString, tenantId))
        await using (var read = CreatePricingReadContext(connectionString, tenantId))
        using (var unitOfWork = new UnitOfWork<PricingDbContext>(write))
        {
            var bus = Substitute.For<IMessageBus>();
            await BasketCheckoutRequestedHandler.Handle(request,
                new PricingReadRepository<Price, Guid>(read),
                new PricingReadRepository<ExchangeRate, Guid>(read),
                new PricingReadRepository<CatalogPrice, Guid>(read),
                new PricingWriteRepository<PendingPriceResolution, Guid>(write, new HttpContextAccessor()),
                unitOfWork, Options.Create(new PricingOptions()), bus, CancellationToken.None);
            await bus.Received(1).PublishAsync(Arg.Is<CatalogPriceReconciliationRequestedIntegrationEvent>(evt => evt.ProductId == productId && evt.TenantId == tenantId));
        }

        await using (var write = CreatePricingWriteContext(connectionString, tenantId))
        await using (var read = CreatePricingReadContext(connectionString, tenantId))
        using (var unitOfWork = new UnitOfWork<PricingDbContext>(write))
        {
            var bus = Substitute.For<IMessageBus>();
            await CatalogPriceReconciledHandler.Handle(new CatalogPriceReconciledIntegrationEvent
            {
                ProductId = productId, VariantId = Guid.NewGuid(), TenantId = tenantId, Amount = 12.50m,
                Currency = "USD", RequestId = requestId, SourceCorrelationId = requestId,
            },
                new PricingWriteRepository<CatalogPrice, Guid>(write, new HttpContextAccessor()),
                new PricingWriteRepository<PendingPriceResolution, Guid>(write, new HttpContextAccessor()),
                new PricingReadRepository<Price, Guid>(read),
                new PricingReadRepository<ExchangeRate, Guid>(read),
                new PricingReadRepository<CatalogPrice, Guid>(read),
                unitOfWork, Options.Create(new PricingOptions()), bus, CancellationToken.None);
            await bus.Received(1).PublishAsync(Arg.Is<BasketPricedIntegrationEvent>(evt => evt.TenantId == tenantId && evt.RequestId == requestId && evt.Amount == 25m));
        }

        await using var verification = CreatePricingReadContext(connectionString, tenantId);
        var resolved = await ResolvePriceHandler.ResolveAsync(new ResolvePriceQuery(productId, "USD", 2, null, null, null, DateTimeOffset.UtcNow),
            new PricingReadRepository<Price, Guid>(verification),
            new PricingReadRepository<ExchangeRate, Guid>(verification),
            new PricingReadRepository<CatalogPrice, Guid>(verification),
            Options.Create(new PricingOptions()), CancellationToken.None);
        Assert.False(resolved.IsError);
        Assert.Equal(12.50m, resolved.Value.UnitAmount);
        Assert.True(await verification.PendingPriceResolutions.SingleAsync(pending => pending.RequestId == requestId).ConfigureAwait(false) is { IsResolved: true });
    }

    /// <summary>Reconciles an existing customer contact by immutable Keycloak subject without a direct service call.</summary>
    [Fact]
    public async Task CustomerBeforeNotificationSubscription_ReconcilesContactForMatchingSubjectOnly()
    {
        const string tenantId = "bootstrap-customer-tenant";
        const string subject = "bootstrap-keycloak-subject";
        string connectionString = await fixture.CreateSharedTestDatabaseAsync(typeof(CustomerDbContext), "Customer.Host");
        var customer = Customer.Create(tenantId, subject, "bootstrap@example.test", "Ada", "Lovelace");
        await using (var write = CreateCustomerWriteContext(connectionString, tenantId))
        {
            write.Set<Customer>().Add(customer);
            await write.SaveChangesAsync();
        }

        await using var read = CreateCustomerReadContext(connectionString, tenantId);
        var bus = Substitute.For<IMessageBus>();
        await CustomerContactReconciliationRequestedHandler.Handle(new CustomerContactReconciliationRequestedIntegrationEvent
        {
            CustomerId = Guid.Empty, KeycloakSubjectId = subject, TenantId = tenantId,
            RequestId = "customer-before-subscription", SourceCorrelationId = "notification-bootstrap",
        }, new CustomerReadRepository<Customer, Guid>(read), bus, CancellationToken.None);

        await bus.Received(1).PublishAsync(Arg.Is<CustomerContactReconciledIntegrationEvent>(evt =>
            evt.CustomerId == customer.Id && evt.KeycloakSubjectId == subject && evt.TenantId == tenantId && evt.Email == customer.Email));
    }

    private static PricingDbContext CreatePricingWriteContext(string connectionString, string tenantId) => new(
        new DbContextOptionsBuilder<PricingDbContext>().UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Pricing.Host")).UseTeckCloudTenant(tenantId).Options,
        null!);

    private static PricingReadDbContext CreatePricingReadContext(string connectionString, string tenantId) => new(
        new DbContextOptionsBuilder<PricingReadDbContext>().UseNpgsql(connectionString).UseTeckCloudTenant(tenantId).Options,
        null!);

    private static CustomerDbContext CreateCustomerWriteContext(string connectionString, string tenantId) => new(
        new DbContextOptionsBuilder<CustomerDbContext>().UseNpgsql(connectionString, npgsql => npgsql.MigrationsAssembly("Customer.Host")).UseTeckCloudTenant(tenantId).Options,
        null!);

    private static CustomerReadDbContext CreateCustomerReadContext(string connectionString, string tenantId) => new(
        new DbContextOptionsBuilder<CustomerReadDbContext>().UseNpgsql(connectionString).UseTeckCloudTenant(tenantId).Options,
        null!);
}
