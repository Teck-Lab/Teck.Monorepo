using System.Security.Cryptography;
using System.Text.Json;
using Customers.Application.Database;
using Customers.Application.EventHandlers.IntegrationEvents;
using Customers.Domain.Entities;
using Customers.Host.Database;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using SharedKernel.Infrastructure.Database.EFCore;
using Teck.Platform.IntegrationTests.Shared;
using Xunit;

namespace Customers.IntegrationTests;

[Collection("SharedTestcontainers")]
public sealed class EnterprisePolicyAttachedTests(SharedTestcontainersFixture fixture)
    : CustomerIntegrationTestBase(fixture)
{
    [Fact]
    public async Task Signed_transition_persists_policy_and_exact_replay_is_idempotent()
    {
        var tenant = Tenant.CreatePendingReservation(Guid.NewGuid(), $"policy-{Guid.NewGuid():N}", "owner-subject");
        await SeedTenantAsync(tenant).ConfigureAwait(false);

        using var billingPrivateKey = RSA.Create(2048);
        using var licensingPrivateKey = RSA.Create(2048);
        using var billingKeys = new SubscriptionPaymentVerificationKeys([billingPrivateKey.ExportSubjectPublicKeyInfoPem()]);
        using var licensingKeys = new EnterprisePolicyDocumentVerificationKeys(
            [new KeyValuePair<string, string?>("licensing-1", licensingPrivateKey.ExportSubjectPublicKeyInfoPem())]);
        var now = DateTimeOffset.UtcNow;
        var payment = new SubscriptionPaymentConfirmedIntegrationEvent
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
            ValidFromUtc = now,
            ValidUntilUtc = now.AddDays(30),
            ConfirmedAtUtc = now,
            MaxLocations = 2,
            DatabaseStrategy = "shared",
            DatabaseProvider = "postgres",
            PolicyVersion = 4,
            EffectiveGracePeriodDays = 7,
            EffectiveBackupRetentionDays = 30,
        };
        payment.Proof = SubscriptionPaymentProof.Sign(payment, billingPrivateKey);
        using (var scope = CreateServiceScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var paymentHandler = new SubscriptionPaymentConfirmedHandler(
                new CustomerWriteRepository<Tenant, Guid>(context, new HttpContextAccessor()),
                new UnitOfWork<CustomerDbContext>(context), new RecordingOrganizationProvisioner(), billingKeys);
            await paymentHandler.Handle(payment, CancellationToken.None).ConfigureAwait(false);
        }

        var policy = new EnterprisePolicyAttachedIntegrationEvent
        {
            TenantId = tenant.Id,
            TenantIdentifier = tenant.Identifier,
            SubscriptionId = payment.SubscriptionId,
            SourceOrderId = payment.SourceOrderId,
            PolicyId = "enterprise",
            PolicyVersion = "v1",
            GracePeriodDays = 9,
            BackupRetentionDays = 90,
            LicensingSigningKeyId = "licensing-1",
            LicenseKind = "custom_enterprise",
            ValidFromUtc = now.ToUniversalTime().AddMinutes(-1),
            ValidUntilUtc = now.ToUniversalTime().AddDays(365),
        };
        policy.LicensingSignature = SignLicensingDocument(policy, licensingPrivateKey);

        policy.Proof = EnterprisePolicyAttachedProof.Sign(policy, billingPrivateKey);
        var wrongOrder = Clone(policy);
        wrongOrder.SourceOrderId = Guid.NewGuid();
        wrongOrder.Proof = EnterprisePolicyAttachedProof.Sign(wrongOrder, billingPrivateKey);
        var wrongSubscription = Clone(policy);
        wrongSubscription.SubscriptionId = Guid.NewGuid();
        wrongSubscription.Proof = EnterprisePolicyAttachedProof.Sign(wrongSubscription, billingPrivateKey);
        var changedLicense = Clone(policy);
        changedLicense.GracePeriodDays++;
        changedLicense.Proof = EnterprisePolicyAttachedProof.Sign(changedLicense, billingPrivateKey);
        var expiredLicense = Clone(policy);
        expiredLicense.ValidFromUtc = now.ToUniversalTime().AddDays(-30);
        expiredLicense.ValidUntilUtc = now.ToUniversalTime().AddMinutes(-1);
        expiredLicense.LicensingSignature = SignLicensingDocument(expiredLicense, licensingPrivateKey);
        expiredLicense.Proof = EnterprisePolicyAttachedProof.Sign(expiredLicense, billingPrivateKey);

        static EnterprisePolicyAttachedIntegrationEvent Clone(EnterprisePolicyAttachedIntegrationEvent source) => new()
        {
            TenantId = source.TenantId,
            TenantIdentifier = source.TenantIdentifier,
            SubscriptionId = source.SubscriptionId,
            SourceOrderId = source.SourceOrderId,
            PolicyId = source.PolicyId,
            PolicyVersion = source.PolicyVersion,
            GracePeriodDays = source.GracePeriodDays,
            BackupRetentionDays = source.BackupRetentionDays,
            LicensingSigningKeyId = source.LicensingSigningKeyId,
            LicenseKind = source.LicenseKind,
            LicensingSignature = source.LicensingSignature,
            ValidFromUtc = source.ValidFromUtc,
            ValidUntilUtc = source.ValidUntilUtc,
            Proof = source.Proof,
        };

        using (var scope = CreateServiceScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var handler = new EnterprisePolicyAttachedHandler(
                new CustomerWriteRepository<Tenant, Guid>(context, new HttpContextAccessor()),
                new UnitOfWork<CustomerDbContext>(context), billingKeys, licensingKeys, new ActivePolicyVerifier());
            await handler.Handle(policy, CancellationToken.None).ConfigureAwait(false);
            await handler.Handle(policy, CancellationToken.None).ConfigureAwait(false);
        }
        using (var scope = CreateServiceScope())
        {
            var context = scope.ServiceProvider.GetRequiredService<CustomerDbContext>();
            var handler = new EnterprisePolicyAttachedHandler(
                new CustomerWriteRepository<Tenant, Guid>(context, new HttpContextAccessor()),
                new UnitOfWork<CustomerDbContext>(context), billingKeys, licensingKeys, new ActivePolicyVerifier());
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(wrongOrder, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(wrongSubscription, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(changedLicense, CancellationToken.None)).ConfigureAwait(false);
            await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(expiredLicense, CancellationToken.None)).ConfigureAwait(false);
        }

        using var rejectionScope = CreateServiceScope();
        var unchanged = await rejectionScope.ServiceProvider.GetRequiredService<CustomerDbContext>().Tenants
            .AsNoTracking().SingleAsync(item => item.Id == tenant.Id).ConfigureAwait(false);
        Assert.Equal(policy.PolicyId, unchanged.EnterprisePolicyId);
        Assert.Equal(policy.Proof, unchanged.EnterprisePolicyTransitionProof);

        using var verifyScope = CreateServiceScope();
        var saved = await verifyScope.ServiceProvider.GetRequiredService<CustomerDbContext>().Tenants
            .AsNoTracking().SingleAsync(item => item.Id == tenant.Id).ConfigureAwait(false);
        Assert.Equal(policy.PolicyId, saved.EnterprisePolicyId);
        Assert.Equal(policy.PolicyVersion, saved.EnterprisePolicyVersion);
        Assert.Equal(policy.GracePeriodDays, saved.EnterprisePolicyGracePeriodDays);
        Assert.Equal(policy.ValidFromUtc.ToUnixTimeMilliseconds(), saved.EnterprisePolicyValidFromUtc?.ToUnixTimeMilliseconds());
        Assert.Equal(policy.ValidUntilUtc.ToUnixTimeMilliseconds(), saved.EnterprisePolicyValidUntilUtc?.ToUnixTimeMilliseconds());
        Assert.Equal(policy.Proof, saved.EnterprisePolicyTransitionProof);
        Assert.Equal(policy.SourceOrderId, saved.EnterprisePolicySourceOrderId);
        Assert.Equal(payment.SubscriptionId, saved.SubscriptionId);
    }

    private static string SignLicensingDocument(EnterprisePolicyAttachedIntegrationEvent policy, RSA key)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            policy_id = policy.PolicyId,
            policy_version = policy.PolicyVersion,
            tenant_id = policy.TenantId,
            subscription_id = policy.SubscriptionId,
            source_order_id = policy.SourceOrderId,
            grace_period_days = policy.GracePeriodDays,
            backup_retention_days = policy.BackupRetentionDays,
            valid_from_utc = policy.ValidFromUtc.ToUniversalTime().ToString("O"),
            valid_until_utc = policy.ValidUntilUtc.ToUniversalTime().ToString("O"),
            license_kind = policy.LicenseKind,
            signing_key_id = policy.LicensingSigningKeyId,
        });
        return Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));
    }

    private sealed class RecordingOrganizationProvisioner : IOrganizationProvisioner
    {
        public Task<Guid> EnsureOwnerOrganizationAsync(Guid tenantId, string identifier, string ownerSubjectId,
            CancellationToken cancellationToken) => Task.FromResult(Guid.NewGuid());
    }
    private sealed class ActivePolicyVerifier : IEnterprisePolicyFreshnessVerifier
    {
        public Task VerifyAsync(EnterprisePolicyAttachedIntegrationEvent transition, CancellationToken cancellationToken) => Task.CompletedTask;
    }
}
