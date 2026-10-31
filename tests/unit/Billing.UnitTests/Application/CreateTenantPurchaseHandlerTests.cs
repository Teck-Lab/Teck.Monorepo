using Billings.Application.Billing.Purchases;
using Billings.Application.Database;
using Billings.Domain.Entities;
using Billing.UnitTests.TestContext;
using Microsoft.Extensions.Options;
using SharedKernel.Infrastructure.Database.EFCore;
using NSubstitute;
using Xunit;

namespace Billing.UnitTests.Application;

public sealed class CreateTenantPurchaseHandlerTests
{
    private static IPurchaseTenantLock PurchaseTenantLock()
    {
        var tenantLock = NSubstitute.Substitute.For<IPurchaseTenantLock>();
        tenantLock.AcquireAsync(Arg.Any<Guid>(), Arg.Any<CancellationToken>()).Returns(true);
        return tenantLock;
    }

    [Fact]
    public async Task Handle_UsesServerPlanPriceAndIsIdempotentForSameTenant()
    {
        await using var context = BillingTestContext.CreateInMemory();
        var repository = BillingTestContext.WriteRepo<TenantPurchaseOrder>(context);
        var unitOfWork = new UnitOfWork<BillingDbContext>(context);
        var handler = new CreateTenantPurchaseHandler(repository, unitOfWork, Options.Create(new TenantPurchaseOptions
        {
            PlanVersion = "2026-10",
            AllowedDatabaseProviders = ["postgres"],
            ReadReplicaMonthlyRate = 15m,
        }), PurchaseTenantLock());
        var tenantId = Guid.NewGuid();
        var command = new CreateTenantPurchaseCommand(tenantId, "acme", "Business", "2026-10", "postgres", true, 2, "buyer", "buyer");

        var first = await handler.Handle(command, CancellationToken.None);
        var retry = await handler.Handle(command, CancellationToken.None);

        Assert.Equal(first.Id, retry.Id);
        Assert.Equal(17999, first.AmountMinor);
        Assert.Equal("USD", first.Currency);
        Assert.Equal("pending", first.Status);
        Assert.Equal("Business", first.PlanId);
        Assert.Equal("2026-10", first.PlanVersion);
    }

    [Fact]
    public async Task Handle_RejectsDifferentTenantAndInvalidPlanVersionOrProvider()
    {
        var name = Guid.NewGuid().ToString("N");
        await using var context = BillingTestContext.CreateInMemory(name);
        var repository = BillingTestContext.WriteRepo<TenantPurchaseOrder>(context);
        var unitOfWork = new UnitOfWork<BillingDbContext>(context);
        var handler = new CreateTenantPurchaseHandler(repository, unitOfWork, Options.Create(new TenantPurchaseOptions
        {
            PlanVersion = "2026-10",
            AllowedDatabaseProviders = ["postgres"],
            ReadReplicaMonthlyRate = 15m,
        }), PurchaseTenantLock());
        var tenantId = Guid.NewGuid();
        var original = new CreateTenantPurchaseCommand(tenantId, "acme", "Business", "2026-10", "postgres", false, 0, "buyer", "buyer");
        await handler.Handle(original, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(original with { BeneficiarySubjectId = "other-buyer" }, CancellationToken.None));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(original with { TenantId = Guid.NewGuid() }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(original with { PlanVersion = "stale" }, CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new CreateTenantPurchaseCommand(Guid.NewGuid(), "other", "Business", "2026-10", "mysql", false, 0, "buyer", "buyer"), CancellationToken.None));
        await Assert.ThrowsAsync<ArgumentException>(() => handler.Handle(new CreateTenantPurchaseCommand(Guid.NewGuid(), "other", "unknown", "2026-10", "postgres", false, 0, "buyer", "buyer"), CancellationToken.None));
    }

    [Fact]
    public async Task Handle_AfterPaidPeriod_CreatesOneServerPricedPendingRenewalForSameOwner()
    {
        await using var context = BillingTestContext.CreateInMemory();
        var repository = BillingTestContext.WriteRepo<TenantPurchaseOrder>(context);
        var unitOfWork = new UnitOfWork<BillingDbContext>(context);
        var handler = new CreateTenantPurchaseHandler(repository, unitOfWork, Options.Create(new TenantPurchaseOptions
        {
            PlanVersion = "2026-10",
            AllowedDatabaseProviders = ["postgres"],
            ReadReplicaMonthlyRate = 15m,
        }), PurchaseTenantLock());
        var command = new CreateTenantPurchaseCommand(Guid.NewGuid(), $"renew-{Guid.NewGuid():N}",
            "Business", "2026-10", "postgres", true, 2, "buyer", "buyer");
        var original = await handler.Handle(command, CancellationToken.None);
        context.TenantPurchaseOrders.Single(order => order.Id == original.Id)
            .ConfirmCapturedPayment(Guid.NewGuid(), DateTimeOffset.UtcNow);
        await context.SaveChangesAsync();

        Assert.Equal(original.Id, (await handler.Handle(command, CancellationToken.None)).Id);
        var renewCommand = command with { IsRenewal = true, PreviousPurchaseOrderId = original.Id };
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            renewCommand with { BeneficiarySubjectId = "intruder" }, CancellationToken.None));
        var renewal = await handler.Handle(renewCommand, CancellationToken.None);
        var retry = await handler.Handle(renewCommand, CancellationToken.None);
        Assert.NotEqual(original.Id, renewal.Id);
        Assert.Equal(original.Id, context.TenantPurchaseOrders.Single(order => order.Id == renewal.Id).PreviousPurchaseOrderId);
        Assert.Equal(renewal.Id, retry.Id);
        Assert.Equal("pending", renewal.Status);
        Assert.Equal(17999, renewal.AmountMinor);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            renewCommand with { PreviousPurchaseOrderId = Guid.NewGuid() }, CancellationToken.None));
    }

}
