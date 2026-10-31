using Billings.Application.Billing.Purchases;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Billings.Host.Infrastructure;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Pricing;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Billing.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class BillingPurgeFenceTests(SharedTestcontainersFixture fixture) : BillingIntegrationTestBase(fixture)
{
    [Fact]
    public async Task AcquirePurgeFence_DeniesBillingPaidSuccessorEvenWhenCustomerProjectionIsStale()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var identifier = $"purge-fence-{tenantId:N}";
        var source = TenantPurchaseOrder.Create(tenantId, identifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow.AddDays(-400), "buyer", "buyer");
        source.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow.AddDays(-400));
        source.Cancel(source.ValidUntilUtc!.Value);
        var successor = TenantPurchaseOrder.Create(tenantId, identifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        successor.BindPreviousPurchaseOrder(source.Id);
        successor.ConfirmRenewalPayment(subscriptionId, source.ValidUntilUtc.Value, DateTimeOffset.UtcNow);
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.AddRange(source, successor);
            await db.SaveChangesAsync();
        }

        await using var verifyScope = Services.CreateAsyncScope();
        var fence = verifyScope.ServiceProvider.GetRequiredService<BillingPurgeFence>();
        var acquired = await fence.TryAcquireAsync(tenantId, subscriptionId, source.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.Null(acquired);
    }

    [Fact]
    public async Task AcquirePurgeFence_DeniesActivePaidTenantEvenWhenSuccessorIsNotDirectChild()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var identifier = $"purge-paid-{tenantId:N}";
        var source = TenantPurchaseOrder.Create(tenantId, identifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow.AddDays(-400), "buyer", "buyer");
        source.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow.AddDays(-400));
        source.Cancel(source.ValidUntilUtc!.Value);
        var paid = TenantPurchaseOrder.Create(tenantId, identifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        paid.ConfirmCapturedPayment(Guid.NewGuid(), DateTimeOffset.UtcNow);
        await using (var seed = Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.AddRange(source, paid);
            await db.SaveChangesAsync();
        }

        await using var scope = Services.CreateAsyncScope();
        var grant = await scope.ServiceProvider.GetRequiredService<BillingPurgeFence>()
            .TryAcquireAsync(tenantId, subscriptionId, source.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.Null(grant);
    }

    [Fact]
    public async Task PurgeFence_AndRealPendingOrderHandler_SerializeControlledTenantInterleaving()
    {
        var tenantId = Guid.NewGuid();
        var source = TenantPurchaseOrder.Create(tenantId, $"purge-handler-{tenantId:N}", TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow.AddDays(-400), "buyer", "buyer");
        source.ConfirmCapturedPayment(Guid.NewGuid(), DateTimeOffset.UtcNow.AddDays(-400));
        source.Cancel(source.ValidUntilUtc!.Value);
        await using (var seed = Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.Add(source);
            await db.SaveChangesAsync();
        }

        var command = new CreateTenantPurchaseCommand(tenantId, source.TenantIdentifier, "Business", "2026-10", "postgres",
            false, 0, "buyer", "buyer");
        await using var createScope = Services.CreateAsyncScope();
        var createDb = createScope.ServiceProvider.GetRequiredService<BillingDbContext>();
        await using var createTransaction = await createDb.Database.BeginTransactionAsync();
        var tenantLock = createScope.ServiceProvider.GetRequiredService<IPurchaseTenantLock>();
        Assert.True(await tenantLock.AcquireAsync(tenantId, CancellationToken.None));
        var created = TenantPurchaseOrder.Create(tenantId, source.TenantIdentifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        createDb.TenantPurchaseOrders.Add(created);
        await createDb.SaveChangesAsync();

        var acquisition = Task.Run(async () =>
        {
            await using var scope = Services.CreateAsyncScope();
            return await scope.ServiceProvider.GetRequiredService<BillingPurgeFence>().TryAcquireAsync(
                tenantId, Guid.NewGuid(), source.Id, Guid.NewGuid(), CancellationToken.None);
        });
        await Task.Delay(250);
        Assert.False(acquisition.IsCompleted);
        await createTransaction.CommitAsync();
        var grant = await acquisition.WaitAsync(TimeSpan.FromSeconds(15));

        await using var verify = Services.CreateAsyncScope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<BillingDbContext>();
        var stored = await dbVerify.TenantPurchaseOrders.SingleAsync(order => order.Id == created.Id);
        var activeGrant = await dbVerify.TenantPurgeGrants.AnyAsync(item => item.TenantId == tenantId && item.CompletedAtUtc == null);
        Assert.Equal("pending", stored.Status);
        Assert.Null(grant);
        Assert.False(activeGrant);
    }

    [Fact]
    public async Task ActivePurgeGrant_RejectsRealPendingOrderHandlerAfterGateCommits()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var source = TenantPurchaseOrder.Create(tenantId, $"purge-existing-{tenantId:N}", TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow.AddDays(-400), "buyer", "buyer");
        source.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow.AddDays(-400));
        source.Cancel(source.ValidUntilUtc!.Value);
        await using (var seed = Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.Add(source);
            await db.SaveChangesAsync();
        }

        await using (var scope = Services.CreateAsyncScope())
        {
            var grant = await scope.ServiceProvider.GetRequiredService<BillingPurgeFence>().TryAcquireAsync(
                tenantId, subscriptionId, source.Id, Guid.NewGuid(), CancellationToken.None);
            Assert.NotNull(grant);
        }

        await using var createScope = Services.CreateAsyncScope();
        var command = new CreateTenantPurchaseCommand(tenantId, source.TenantIdentifier, "Business", "2026-10", "postgres",
            false, 0, "buyer", "buyer");
        await Assert.ThrowsAsync<InvalidOperationException>(() => createScope.ServiceProvider
            .GetRequiredService<Wolverine.IMessageBus>().InvokeAsync<TenantPurchaseOrderDto>(command));
    }

    [Fact]
    public async Task CompletedPurgeGrant_RemainsTerminalForItsSourceOrder()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var source = TenantPurchaseOrder.Create(tenantId, $"purge-terminal-{tenantId:N}", TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow.AddDays(-400), "buyer", "buyer");
        source.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow.AddDays(-400));
        source.Cancel(source.ValidUntilUtc!.Value);
        await using (var seed = Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.Add(source);
            await db.SaveChangesAsync();
        }

        var approvalId = Guid.NewGuid();
        Guid grantId;
        await using (var scope = Services.CreateAsyncScope())
        {
            grantId = (await scope.ServiceProvider.GetRequiredService<BillingPurgeFence>().TryAcquireAsync(
                tenantId, subscriptionId, source.Id, approvalId, CancellationToken.None)).GetValueOrDefault();
        }
        await using (var restartedWorker = Services.CreateAsyncScope())
        {
            var sameApprovalGrant = await restartedWorker.ServiceProvider.GetRequiredService<BillingPurgeFence>()
                .TryAcquireAsync(tenantId, subscriptionId, source.Id, approvalId, CancellationToken.None);
            Assert.Equal(grantId, sameApprovalGrant);
            Assert.Null(await restartedWorker.ServiceProvider.GetRequiredService<BillingPurgeFence>()
                .TryAcquireAsync(tenantId, subscriptionId, source.Id, Guid.NewGuid(), CancellationToken.None));
        }
        Assert.NotEqual(Guid.Empty, grantId);

        await using (var scope = Services.CreateAsyncScope())
        {
            Assert.True(await scope.ServiceProvider.GetRequiredService<BillingPurgeFence>()
                .CompleteAsync(grantId, CancellationToken.None));
        }
        await using var retry = Services.CreateAsyncScope();
        Assert.Null(await retry.ServiceProvider.GetRequiredService<BillingPurgeFence>().TryAcquireAsync(
            tenantId, subscriptionId, source.Id, Guid.NewGuid(), CancellationToken.None));
        await using var purchaseScope = Services.CreateAsyncScope();
        var command = new CreateTenantPurchaseCommand(tenantId, source.TenantIdentifier, "Business", "2026-10", "postgres",
            false, 0, "buyer", "buyer");
        await Assert.ThrowsAsync<InvalidOperationException>(() => purchaseScope.ServiceProvider
            .GetRequiredService<Wolverine.IMessageBus>().InvokeAsync<TenantPurchaseOrderDto>(command));
    }

    [Fact]
    public async Task PurgeFence_DeniesProviderAttemptWhoseStripeIntentIdWasNeverBound()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var source = TenantPurchaseOrder.Create(tenantId, $"purge-unbound-{tenantId:N}", TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow.AddDays(-400), "buyer", "buyer");
        source.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow.AddDays(-400));
        source.Cancel(source.ValidUntilUtc!.Value);
        var pending = TenantPurchaseOrder.Create(tenantId, source.TenantIdentifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        var attemptedAt = DateTimeOffset.UtcNow;
        pending.ReserveStripeIntentAttempt(attemptedAt);
        await using (var seed = Services.CreateAsyncScope())
        {
            var db = seed.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.AddRange(source, pending);
            await db.SaveChangesAsync();
        }

        await using var verify = Services.CreateAsyncScope();
        var dbVerify = verify.ServiceProvider.GetRequiredService<BillingDbContext>();
        var reloaded = await dbVerify.TenantPurchaseOrders.SingleAsync(item => item.Id == pending.Id);
        Assert.True(reloaded.StripeIntentAttempted);
        Assert.Equal(attemptedAt.UtcTicks / 10, reloaded.StripeIntentAttemptedAtUtc!.Value.UtcTicks / 10);
        Assert.Null(reloaded.StripePaymentIntentId);
        Assert.Null(await verify.ServiceProvider.GetRequiredService<BillingPurgeFence>().TryAcquireAsync(
            tenantId, subscriptionId, source.Id, Guid.NewGuid(), CancellationToken.None));
    }

    [Fact]
    public async Task PurgeFence_DeniesPendingStripeIntentWithoutAssumingProviderOutcome()
    {
        var tenantId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var source = TenantPurchaseOrder.Create(tenantId, $"purge-intent-{tenantId:N}", TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow.AddDays(-400), "buyer", "buyer");
        source.ConfirmCapturedPayment(subscriptionId, DateTimeOffset.UtcNow.AddDays(-400));
        source.Cancel(source.ValidUntilUtc!.Value);
        var pending = TenantPurchaseOrder.Create(tenantId, source.TenantIdentifier, TenantPlan.Business,
            "2026-10", DatabaseStrategy.Dedicated, "postgres", false, 0,
            new Dictionary<string, decimal>(), DateTimeOffset.UtcNow, "buyer", "buyer");
        pending.ReserveStripeIntentAttempt(DateTimeOffset.UtcNow);
        pending.BindStripePaymentIntent("pi_provider_outcome_unknown");
        await using (var scope = Services.CreateAsyncScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<BillingDbContext>();
            db.TenantPurchaseOrders.AddRange(source, pending);
            await db.SaveChangesAsync();
        }

        await using var verifyScope = Services.CreateAsyncScope();
        var grant = await verifyScope.ServiceProvider.GetRequiredService<BillingPurgeFence>().TryAcquireAsync(
            tenantId, subscriptionId, source.Id, Guid.NewGuid(), CancellationToken.None);
        Assert.Null(grant);
    }
}
