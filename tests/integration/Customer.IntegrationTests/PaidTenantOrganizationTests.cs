using Customers.Application.Database;
using Customers.Application.EventHandlers.IntegrationEvents;
using Customers.Domain.Entities;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Database;
using Customers.Host.Database;
using SharedKernel.Infrastructure.Database.EFCore;
using SharedKernel.Events;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Customers.IntegrationTests;

/// <summary>Proves payment precedes organization membership and failures remain retryable.</summary>
[Collection("SharedTestcontainers")]
public sealed class PaidTenantOrganizationTests(SharedTestcontainersFixture fixture) : CustomerIntegrationTestBase(fixture)
{
    private static SubscriptionPaymentConfirmedHandler CreatePaymentHandler(CustomerDbContext context,
        IOrganizationProvisioner organizations, SubscriptionPaymentVerificationKeys billingKeys) =>
        new(new CustomerWriteRepository<Tenant, Guid>(context, new Microsoft.AspNetCore.Http.HttpContextAccessor()),
            new UnitOfWork<CustomerDbContext>(context), organizations, billingKeys);
    [Fact]
    public async Task PaidConfirmation_OrganizationFailureRetainsInaccessiblePaidTenantAndRetryLinksOwner()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), $"business-{Guid.NewGuid():N}", "owner-subject");
        await SeedTenantAsync(tenant).ConfigureAwait(false);
        using var billingPrivateKey = RSA.Create(2048);
        using var billingKeys = new SubscriptionPaymentVerificationKeys([billingPrivateKey.ExportSubjectPublicKeyInfoPem()]);
        var confirmedAt = DateTimeOffset.UtcNow;
        var message = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            TenantId = tenant.Id,
            TenantIdentifier = tenant.Identifier,
            SourceOrderId = Guid.NewGuid(),
            SubscriptionId = Guid.NewGuid(),
            PlanId = "Business",
            PlanVersion = "2026-10",
            PaymentMethod = "captured",
            AmountMinor = 10000,
            Currency = "USD",
            ValidFromUtc = confirmedAt,
            ValidUntilUtc = confirmedAt.AddDays(30),
            ConfirmedAtUtc = confirmedAt,
            MaxLocations = 2,
            DatabaseStrategy = "shared",
            DatabaseProvider = "postgres",
        };
        using (var unsignedScope = CreateServiceScope())
        {
            var unsignedHandler = CreatePaymentHandler(unsignedScope.ServiceProvider.GetRequiredService<CustomerDbContext>(), new RecordingOrganizationProvisioner(), billingKeys);
            await Assert.ThrowsAsync<InvalidOperationException>(() => unsignedHandler.Handle(message, CancellationToken.None)).ConfigureAwait(false);
        }

        using (var pendingScope = CreateServiceScope())
        {
            var pending = await pendingScope.ServiceProvider.GetRequiredService<CustomerDbContext>().Tenants
                .SingleAsync(item => item.Id == tenant.Id).ConfigureAwait(false);
            Assert.Equal("pending", pending.Status);
            Assert.Equal(Guid.Empty, pending.PurchaseOrderId);
        }
        message.Proof = SubscriptionPaymentProof.Sign(message, billingPrivateKey);

        var provisioner = new RecordingOrganizationProvisioner { Fail = true };
        using (var scope = CreateServiceScope())
        {
            var handler = CreatePaymentHandler(scope.ServiceProvider.GetRequiredService<CustomerDbContext>(), provisioner, billingKeys);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(message, CancellationToken.None)).ConfigureAwait(false);
        }

        using (var scope = CreateServiceScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Tenants
                .SingleAsync(item => item.Id == tenant.Id).ConfigureAwait(false);
            Assert.Equal("paid_pending_readiness", saved.Status);
            Assert.Null(saved.KeycloakOrganizationId);
            Assert.Equal(message.SubscriptionId, saved.SubscriptionId);
            Assert.Equal(message.ValidUntilUtc.UtcTicks / 10, saved.ValidUntilUtc?.UtcTicks / 10);
        }

        provisioner.Fail = false;
        using (var scope = CreateServiceScope())
        {
            var handler = CreatePaymentHandler(scope.ServiceProvider.GetRequiredService<CustomerDbContext>(), provisioner, billingKeys);
            await handler.Handle(message, CancellationToken.None).ConfigureAwait(false);
            await handler.Handle(message, CancellationToken.None).ConfigureAwait(false);
            var originalValidUntilUtc = message.ValidUntilUtc;
            message.ValidUntilUtc = originalValidUntilUtc.AddDays(1);
            message.Proof = SubscriptionPaymentProof.Sign(message, billingPrivateKey);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(message, CancellationToken.None)).ConfigureAwait(false);
            message.ValidUntilUtc = originalValidUntilUtc;
            message.Proof = SubscriptionPaymentProof.Sign(message, billingPrivateKey);
            message.SubscriptionId = Guid.NewGuid();
            message.Proof = SubscriptionPaymentProof.Sign(message, billingPrivateKey);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(message, CancellationToken.None)).ConfigureAwait(false);
        }

        using (var scope = CreateServiceScope())
        {
            var saved = await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Tenants
                .SingleAsync(item => item.Id == tenant.Id).ConfigureAwait(false);
            Assert.Equal("paid_pending_readiness", saved.Status);
            Assert.Equal(provisioner.OrganizationId, saved.KeycloakOrganizationId);
            Assert.Equal(tenant.Id, provisioner.LinkedTenantId);
            Assert.Equal("owner-subject", provisioner.LinkedOwnerSubjectId);
            Assert.Equal(2, provisioner.Calls);
        }
    }

    [Fact]
    public async Task Signed_payment_replay_after_suspension_cannot_rejoin_owner_or_reactivate_tenant()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), $"suspended-{Guid.NewGuid():N}", "owner-subject");
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var suspendedAt = DateTimeOffset.UtcNow;
        tenant.SuspendSubscription(orderId, subscriptionId, suspendedAt);
        await SeedTenantAsync(tenant).ConfigureAwait(false);
        using var billingPrivateKey = RSA.Create(2048);
        using var trustedKeys = new SubscriptionPaymentVerificationKeys([billingPrivateKey.ExportSubjectPublicKeyInfoPem()]);
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            TenantId = tenant.Id, TenantIdentifier = tenant.Identifier,
            SourceOrderId = orderId, SubscriptionId = subscriptionId,
            PlanId = "Business", PlanVersion = "2026-10", PaymentMethod = "captured",
            AmountMinor = 10000, Currency = "USD", DatabaseStrategy = "shared", DatabaseProvider = "postgres",
            MaxLocations = 2, ConfirmedAtUtc = suspendedAt.AddMinutes(-1),
            ValidFromUtc = suspendedAt.AddMinutes(-1), ValidUntilUtc = suspendedAt.AddYears(1),
        };
        paid.Proof = SubscriptionPaymentProof.Sign(paid, billingPrivateKey);
        var provisioner = new RecordingOrganizationProvisioner();

        using (var scope = CreateServiceScope())
        {
            var handler = CreatePaymentHandler(scope.ServiceProvider.GetRequiredService<CustomerDbContext>(), provisioner, trustedKeys);
            await handler.Handle(paid, CancellationToken.None).ConfigureAwait(false);
        }

        using (var scope = CreateServiceScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var stored = await db.Tenants.SingleAsync(item => item.Id == tenant.Id).ConfigureAwait(false);
            stored.SuspendSubscription(orderId, subscriptionId, suspendedAt);
            await db.SaveChangesAsync().ConfigureAwait(false);
            Assert.Equal("suspended", stored.Status);
            Assert.Equal(suspendedAt.ToUnixTimeMilliseconds(), stored.SuspendedAtUtc?.ToUnixTimeMilliseconds());
            Assert.Null(stored.KeycloakOrganizationId);
            Assert.False(stored.LicenseReady);
            Assert.Equal(0, provisioner.Calls);
        }
    }

    [Fact]
    public async Task Signed_cancellation_suspends_active_tenant_and_rejects_forged_or_changed_reason()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), $"cancelled-{Guid.NewGuid():N}", "owner-subject");
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        tenant.ApplyPurchaseConfirmation(orderId, subscriptionId, "Business", "2026-10", "shared", "postgres", false, DateTimeOffset.UtcNow.AddYears(1));
        tenant.MarkProvisionerReady("license");
        tenant.MarkProvisionerReady("database");
        tenant.LinkOrganization(Guid.NewGuid());
        await SeedTenantAsync(tenant).ConfigureAwait(false);
        using var billingPrivateKey = RSA.Create(2048);
        using var billingKeys = new SubscriptionPaymentVerificationKeys([billingPrivateKey.ExportSubjectPublicKeyInfoPem()]);
        var suspendedAt = DateTimeOffset.UtcNow;
        var cancellation = new SubscriptionAccessSuspendedIntegrationEvent
        {
            TenantId = tenant.Id, TenantIdentifier = tenant.Identifier,
            SourceOrderId = orderId, SubscriptionId = subscriptionId,
            Reason = "cancelled", SuspendedAtUtc = suspendedAt,
        };
        using (var scope = CreateServiceScope())
        {
            var handler = new SubscriptionAccessSuspendedHandler(
                scope.ServiceProvider.GetRequiredService<IGenericWriteRepository<Tenant, Guid>>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), billingKeys);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(cancellation, CancellationToken.None)).ConfigureAwait(false);
        }

        cancellation.Proof = SubscriptionSuspensionProof.Sign(cancellation, billingPrivateKey);
        using (var scope = CreateServiceScope())
        {
            var handler = new SubscriptionAccessSuspendedHandler(
                scope.ServiceProvider.GetRequiredService<IGenericWriteRepository<Tenant, Guid>>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), billingKeys);
            await handler.Handle(cancellation, CancellationToken.None).ConfigureAwait(false);
            await handler.Handle(cancellation, CancellationToken.None).ConfigureAwait(false);
            cancellation.Reason = "renewal_failed";
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(cancellation, CancellationToken.None)).ConfigureAwait(false);
        }

        using (var scope = CreateServiceScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Tenants
                .SingleAsync(item => item.Id == tenant.Id).ConfigureAwait(false);
            Assert.Equal("suspended", stored.Status);
            Assert.False(stored.LicenseReady);
            Assert.False(stored.DatabaseReady);
            Assert.Equal(suspendedAt.ToUnixTimeMilliseconds(), stored.SuspendedAtUtc?.ToUnixTimeMilliseconds());
        }
    }

    [Fact]
    public async Task Readiness_WithoutCorrelatedConfirmedOrderAndSubscription_CannotActivateTenant()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), $"readiness-{Guid.NewGuid():N}", "owner-subject");
        await SeedTenantAsync(tenant).ConfigureAwait(false);
        using var scope = CreateServiceScope();
        var db = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
        var stored = await db.Tenants.SingleAsync(item => item.Id == tenant.Id).ConfigureAwait(false);
        stored.ApplyPurchaseConfirmation(Guid.NewGuid(), Guid.NewGuid(), "Business", "2026-10", "shared", "postgres", false, DateTimeOffset.UtcNow.AddYears(1));
        stored.LinkOrganization(Guid.NewGuid());
        await db.SaveChangesAsync().ConfigureAwait(false);
        var licenseKey = Encoding.UTF8.GetBytes("test-license-proof-key-32-bytes-minimum");
        var databaseKey = Encoding.UTF8.GetBytes("test-database-proof-key-32-bytes-minimum");
        var proofConfig = new ConfigurationBuilder().AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Readiness:LicenseKey"] = Convert.ToBase64String(licenseKey),
            ["Readiness:DatabaseKey"] = Convert.ToBase64String(databaseKey),
        }).Build();
        var handler = new TenantProvisioningReadyHandler(
            scope.ServiceProvider.GetRequiredService<IGenericWriteRepository<Tenant, Guid>>(),
            scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), new TenantReadinessVerifier(proofConfig));

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new TenantProvisioningReadyIntegrationEvent { TenantId = tenant.Id, Provisioner = "license" },
            CancellationToken.None)).ConfigureAwait(false);
        Assert.False(stored.LicenseReady);
        Assert.Equal("paid_pending_readiness", stored.Status);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new TenantProvisioningReadyIntegrationEvent
            {
                TenantId = tenant.Id, SourceOrderId = Guid.NewGuid(), SubscriptionId = stored.SubscriptionId,
                Provisioner = "database",
            }, CancellationToken.None)).ConfigureAwait(false);
        Assert.False(stored.DatabaseReady);

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(
            new TenantProvisioningReadyIntegrationEvent
            {
                TenantId = tenant.Id, SourceOrderId = stored.PurchaseOrderId, SubscriptionId = Guid.NewGuid(),
                Provisioner = "license",
            }, CancellationToken.None)).ConfigureAwait(false);
        Assert.False(stored.LicenseReady);

        var license = new TenantProvisioningReadyIntegrationEvent
        {
            TenantId = tenant.Id, SourceOrderId = stored.PurchaseOrderId,
            SubscriptionId = stored.SubscriptionId, Provisioner = "license", EvidenceId = Guid.NewGuid(),
        };
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(license, CancellationToken.None)).ConfigureAwait(false);
        Assert.False(stored.LicenseReady);
        license.Proof = TenantReadinessProof.Sign(license, databaseKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(license, CancellationToken.None)).ConfigureAwait(false);
        Assert.False(stored.LicenseReady);
        license.Proof = TenantReadinessProof.Sign(license, licenseKey);
        await handler.Handle(license, CancellationToken.None).ConfigureAwait(false);
        Assert.True(stored.LicenseReady);
        Assert.Equal("paid_pending_readiness", stored.Status);
        var databaseReady = new TenantProvisioningReadyIntegrationEvent
        {
            TenantId = tenant.Id, SourceOrderId = stored.PurchaseOrderId,
            SubscriptionId = stored.SubscriptionId, Provisioner = "database", EvidenceId = Guid.NewGuid(),
        };
        databaseReady.Proof = TenantReadinessProof.Sign(databaseReady, databaseKey);
        await handler.Handle(databaseReady, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal("active", stored.Status);
        await handler.Handle(license, CancellationToken.None).ConfigureAwait(false);
        Assert.Equal("active", stored.Status);
    }

    [Fact]
    public async Task SignedRenewal_CorrelatesPaidPredecessorAndRejectsStaleReplayWithoutOwnerReprovision()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), $"renewal-{Guid.NewGuid():N}", "owner-subject");
        var oldOrder = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var oldExpiry = DateTimeOffset.UtcNow.AddDays(25);
        tenant.ApplyPurchaseConfirmation(oldOrder, subscription, "Business", "2026-10", "shared", "postgres", false, oldExpiry);
        tenant.LinkOrganization(Guid.NewGuid());
        tenant.MarkProvisionerReady("license");
        tenant.MarkProvisionerReady("database");
        await SeedTenantAsync(tenant);
        using var privateKey = RSA.Create(2048);
        using var keys = new SubscriptionPaymentVerificationKeys([privateKey.ExportSubjectPublicKeyInfoPem()]);
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            TenantId = tenant.Id, TenantIdentifier = tenant.Identifier, SourceOrderId = Guid.NewGuid(),
            PreviousSourceOrderId = oldOrder, SubscriptionId = subscription,
            PlanId = "Business", PlanVersion = "2026-10", PaymentMethod = "captured",
            AmountMinor = 14999, Currency = "USD", DatabaseStrategy = "shared", DatabaseProvider = "postgres",
            ConfirmedAtUtc = DateTimeOffset.UtcNow, ValidFromUtc = oldExpiry, ValidUntilUtc = oldExpiry.AddYears(1),
        };
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);
        var organizations = new RecordingOrganizationProvisioner { Fail = true };
        using (var scope = CreateServiceScope())
        {
            var handler = CreatePaymentHandler(scope.ServiceProvider.GetRequiredService<CustomerDbContext>(), organizations, keys);
            await handler.Handle(paid, CancellationToken.None);
            await handler.Handle(paid, CancellationToken.None);
        }

        using (var scope = CreateServiceScope())
        {
            var stored = await scope.ServiceProvider.GetRequiredService<CustomerDbContext>().Tenants
                .SingleAsync(item => item.Id == tenant.Id);
            Assert.Equal(paid.SourceOrderId, stored.PurchaseOrderId);
            Assert.Equal("paid_pending_readiness", stored.Status);
            Assert.False(stored.LicenseReady);
            Assert.False(stored.DatabaseReady);
            Assert.Equal(0, organizations.Calls);
        }

        paid.PreviousSourceOrderId = Guid.NewGuid();
        Assert.False(keys.Verify(paid));
        paid.Proof = SubscriptionPaymentProof.Sign(paid, privateKey);
        using (var scope = CreateServiceScope())
        {
            var handler = CreatePaymentHandler(scope.ServiceProvider.GetRequiredService<CustomerDbContext>(), organizations, keys);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(paid, CancellationToken.None));
        }
    }

    [Fact]
    public async Task StaleOldPeriodSuspension_CannotRevokeReadyNewerPaidPeriod()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), $"protected-{Guid.NewGuid():N}", "owner-subject");
        var oldOrder = Guid.NewGuid();
        var subscription = Guid.NewGuid();
        var oldExpiry = DateTimeOffset.UtcNow.AddDays(-1);
        tenant.ApplyPurchaseConfirmation(oldOrder, subscription, "Business", "2026-10", "shared", "postgres", false, oldExpiry);
        tenant.LinkOrganization(Guid.NewGuid());
        tenant.MarkProvisionerReady("license");
        tenant.MarkProvisionerReady("database");
        var renewal = Guid.NewGuid();
        tenant.ApplyRenewalConfirmation(oldOrder, renewal, subscription, "Business", "2026-10", "shared", "postgres", false, DateTimeOffset.UtcNow.AddYears(1));
        tenant.MarkProvisionerReady("license");
        tenant.MarkProvisionerReady("database");
        await SeedTenantAsync(tenant);
        using var privateKey = RSA.Create(2048);
        using var keys = new SubscriptionPaymentVerificationKeys([privateKey.ExportSubjectPublicKeyInfoPem()]);
        var stale = new SubscriptionAccessSuspendedIntegrationEvent
        {
            TenantId = tenant.Id, TenantIdentifier = tenant.Identifier,
            SubscriptionId = subscription, SourceOrderId = oldOrder,
            SuspendedAtUtc = oldExpiry, Reason = "renewal_failed",
        };
        stale.Proof = SubscriptionSuspensionProof.Sign(stale, privateKey);
        using (var scope = CreateServiceScope())
        {
            var handler = new SubscriptionAccessSuspendedHandler(
                scope.ServiceProvider.GetRequiredService<IGenericWriteRepository<Tenant, Guid>>(),
                scope.ServiceProvider.GetRequiredService<IUnitOfWork>(), keys);
            await handler.Handle(stale, CancellationToken.None);
        }

        using var verify = CreateServiceScope();
        var saved = await verify.ServiceProvider.GetRequiredService<CustomerDbContext>().Tenants
            .SingleAsync(item => item.Id == tenant.Id);
        Assert.Equal("active", saved.Status);
        Assert.Equal(renewal, saved.PurchaseOrderId);
        Assert.True(saved.LicenseReady && saved.DatabaseReady);
    }

    private sealed class RecordingOrganizationProvisioner : IOrganizationProvisioner
    {
        public Guid OrganizationId { get; } = Guid.NewGuid();
        public string? LinkedOwnerSubjectId { get; private set; }
        public Guid LinkedTenantId { get; private set; }
        public bool Fail { get; set; }
        public int Calls { get; private set; }

        public Task<Guid> EnsureOwnerOrganizationAsync(Guid tenantId, string identifier, string ownerSubjectId, CancellationToken cancellationToken)
        {
            Calls++;
            if (Fail)
            {
                throw new InvalidOperationException("Keycloak admin is unavailable.");
            }

            LinkedTenantId = tenantId;
            LinkedOwnerSubjectId = ownerSubjectId;
            return Task.FromResult(OrganizationId);
        }
    }
}
