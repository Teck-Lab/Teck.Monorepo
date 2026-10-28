using Billings.Application.Database;
using Billings.Domain.Entities;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Pricing;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Billing.IntegrationTests;

/// <summary>Checks that Billing persists distinct paid annual periods for the same tenant and subscription.</summary>
[Collection("SharedTestcontainers")]
public sealed class PurchaseRenewalPeriodTests(SharedTestcontainersFixture fixture) : BillingIntegrationTestBase(fixture)
{
    [Fact]
    public async Task PaidRenewal_PersistsSecondOrderWithSameSubscriptionAndDistinctPaidPeriod()
    {
        var tenantId = Guid.NewGuid();
        var identifier = $"renewal-{tenantId:N}";
        TenantPurchaseOrder Create() => TenantPurchaseOrder.Create(tenantId, identifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        var first = Create();
        var second = Create();
        var subscriptionId = Guid.NewGuid();
        var firstStart = DateTimeOffset.UtcNow;
        first.ConfirmCapturedPayment(subscriptionId, firstStart);
        second.BindPreviousPurchaseOrder(first.Id);
        second.ConfirmRenewalPayment(subscriptionId, first.ValidUntilUtc!.Value, firstStart.AddMonths(11));

        await using (var scope = Services.CreateAsyncScope())
        {
            var database = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            database.TenantPurchaseOrders.AddRange(first, second);
            await database.SaveChangesAsync().ConfigureAwait(false);
        }

        await using var verifyScope = Services.CreateAsyncScope();
        var stored = await verifyScope.ServiceProvider.GetRequiredService<BillingDbContext>().TenantPurchaseOrders
            .Where(order => order.TenantId == tenantId).OrderBy(order => order.ValidFromUtc).ToListAsync().ConfigureAwait(false);
        Assert.Equal([first.Id, second.Id], stored.Select(order => order.Id));
        Assert.All(stored, order => Assert.Equal(subscriptionId, order.SubscriptionId));
        Assert.Equal(first.Id, stored[1].PreviousPurchaseOrderId);
        Assert.Equal(stored[0].ValidUntilUtc, stored[1].ValidFromUtc);
        Assert.Equal(stored[0].ValidUntilUtc!.Value.AddYears(1), stored[1].ValidUntilUtc);
        var current = await new Billings.Application.Billing.Purchases.GetTenantPurchaseOrderHandler(
            verifyScope.ServiceProvider.GetRequiredService<BillingDbContext>())
            .Handle(new Billings.Application.Billing.Purchases.GetTenantPurchaseOrderQuery(tenantId, "buyer"), CancellationToken.None);
        Assert.Equal(second.Id, current?.Id);
    }

    [Fact]
    public async Task TwoPendingOrdersForSameTenant_CannotBothBecomePayable()
    {
        var tenantId = Guid.NewGuid();
        var slug = $"pending-{tenantId:N}";
        TenantPurchaseOrder Create() => TenantPurchaseOrder.Create(tenantId, slug, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        await using var scope = Services.CreateAsyncScope();
        var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
        db.TenantPurchaseOrders.AddRange(Create(), Create());

        await Assert.ThrowsAnyAsync<DbUpdateException>(() => db.SaveChangesAsync());
    }

    [Fact]
    public async Task PrepaidRenewal_PreventsExpirySweepFromSuspendingOldOrder()
    {
        _ = Client;
        var tenantId = Guid.NewGuid();
        var identifier = $"extended-{tenantId:N}";
        TenantPurchaseOrder Create() => TenantPurchaseOrder.Create(tenantId, identifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        var first = Create();
        var second = Create();
        var firstStart = DateTimeOffset.UtcNow.AddYears(-1).AddDays(-1);
        var subscriptionId = Guid.NewGuid();
        first.ConfirmCapturedPayment(subscriptionId, firstStart);
        second.BindPreviousPurchaseOrder(first.Id);
        second.ConfirmRenewalPayment(subscriptionId, first.ValidUntilUtc!.Value, firstStart.AddMonths(11));
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.AddRange(first, second);
            await db.SaveChangesAsync();
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
            await bus.InvokeAsync(new Billings.Application.Billing.Purchases.SuspendExpiredPurchasesCommand(DateTimeOffset.UtcNow));
        }

        await using var verify = Services.CreateAsyncScope();
        var actual = await verify.ServiceProvider.GetRequiredService<BillingDbContext>().TenantPurchaseOrders
            .SingleAsync(item => item.Id == first.Id);
        Assert.Equal("active", actual.Status);
    }

    [Fact]
    public async Task SupersededOrder_CannotCancelNewPaidPeriod()
    {
        var tenantId = Guid.NewGuid();
        var identifier = $"cancel-old-{tenantId:N}";
        TenantPurchaseOrder Create() => TenantPurchaseOrder.Create(tenantId, identifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        var first = Create();
        var second = Create();
        var subscriptionId = Guid.NewGuid();
        first.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow.AddMonths(-11));
        second.BindPreviousPurchaseOrder(first.Id);
        second.ConfirmRenewalPayment(subscriptionId, first.ValidUntilUtc!.Value, DateTimeOffset.UtcNow);
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.AddRange(first, second);
            await db.SaveChangesAsync();
        }

        using var request = new HttpRequestMessage(HttpMethod.Post, $"/purchases/{first.Id}/cancel");
        request.Headers.Add("X-Test-Onboarding-Subject", "buyer");
        using var response = await Client.SendAsync(request);
        Assert.Equal(System.Net.HttpStatusCode.Conflict, response.StatusCode);
        await using var verify = Services.CreateAsyncScope();
        Assert.Equal("active", (await verify.ServiceProvider.GetRequiredService<BillingDbContext>()
            .TenantPurchaseOrders.SingleAsync(item => item.Id == first.Id)).Status);
    }

    [Fact]
    public async Task CapturedRenewal_ReusesSubscriptionAndSignsNewSourceOrderPeriod()
    {
        _ = Client;
        var tenantId = Guid.NewGuid();
        var identifier = $"capture-{tenantId:N}";
        TenantPurchaseOrder Create() => TenantPurchaseOrder.Create(tenantId, identifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        var original = Create();
        var subscriptionId = Guid.NewGuid();
        original.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow.AddMonths(-11));
        var renewal = Create();
        renewal.BindPreviousPurchaseOrder(original.Id);
        var intent = await StripePurchases.CreateAsync(renewal, CancellationToken.None);
        renewal.BindStripePaymentIntent(intent.PaymentIntentId);
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.AddRange(original, renewal);
            await db.SaveChangesAsync();
        }

        StripePurchases.MarkCaptured(intent.PaymentIntentId);
        await using (var scope = Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
            Assert.True(await bus.InvokeAsync<bool>(new Billings.Application.Billing.Purchases.ConfirmStripePurchaseCommand(intent.PaymentIntentId)));
        }

        await using var verify = Services.CreateAsyncScope();
        var confirmed = await verify.ServiceProvider.GetRequiredService<BillingDbContext>().TenantPurchaseOrders
            .SingleAsync(item => item.Id == renewal.Id);
        Assert.Equal("active", confirmed.Status);
        Assert.Equal(subscriptionId, confirmed.SubscriptionId);
        Assert.Equal(original.ValidUntilUtc!.Value.UtcTicks / 10, confirmed.ValidFromUtc!.Value.UtcTicks / 10);
        Assert.Equal(original.ValidUntilUtc.Value.AddYears(1).UtcTicks / 10, confirmed.ValidUntilUtc!.Value.UtcTicks / 10);
    }

    [Fact]
    public async Task AuditedInvoiceRenewal_ReusesSubscriptionAndPreservesInvoiceEvidence()
    {
        _ = Client;
        var tenantId = Guid.NewGuid();
        var identifier = $"invoice-renewal-{tenantId:N}";
        TenantPurchaseOrder Create() => TenantPurchaseOrder.Create(tenantId, identifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "sales", "buyer");
        var original = Create();
        var subscriptionId = Guid.NewGuid();
        original.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow.AddMonths(-11));
        var renewal = Create();
        renewal.BindPreviousPurchaseOrder(original.Id);
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.AddRange(original, renewal);
            await db.SaveChangesAsync();
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var bus = scope.ServiceProvider.GetRequiredService<Wolverine.IMessageBus>();
            var result = await bus.InvokeAsync<TenantPurchaseOrder?>(new Billings.Application.Billing.Purchases.ConfirmExternalInvoiceCommand(
                renewal.Id, "INV-2026-0107", "verified-manager"));
            Assert.NotNull(result);
        }

        await using var verify = Services.CreateAsyncScope();
        var confirmed = await verify.ServiceProvider.GetRequiredService<BillingDbContext>().TenantPurchaseOrders
            .SingleAsync(item => item.Id == renewal.Id);
        Assert.Equal(subscriptionId, confirmed.SubscriptionId);
        Assert.Equal(original.ValidUntilUtc!.Value.UtcTicks / 10, confirmed.ValidFromUtc!.Value.UtcTicks / 10);
        Assert.Equal("external_invoice", confirmed.PaymentMethod);
        Assert.Equal("INV-2026-0107", confirmed.ExternalInvoiceReference);
        Assert.Equal("verified-manager", confirmed.InvoiceVerifiedBy);
    }

}

