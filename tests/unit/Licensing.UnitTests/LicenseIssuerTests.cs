using System.Text;
using System.Security.Cryptography;
using SharedKernel.Events;
using Licensing.Application;
using Standard.Licensing.Security.Cryptography;
using Xunit;

namespace Licensing.UnitTests;

public sealed class LicenseIssuerTests
{
    [Fact]
    public async Task Issue_persists_and_validates_tenant_bound_signed_license_idempotently()
    {
        var (issuer, store, key) = CreateIssuer();
        var subscriptionId = Guid.NewGuid();
        var sourceOrderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var license = await issuer.IssueAsync(sourceOrderId, subscriptionId, tenantId, "starter", "v1", new Dictionary<string, string> { ["analytics"] = "enabled" }, now, TimeSpan.FromDays(30), CancellationToken.None);
        var duplicate = await issuer.IssueAsync(sourceOrderId, subscriptionId, tenantId, "starter", "v1", new Dictionary<string, string> { ["analytics"] = "enabled" }, now, TimeSpan.FromDays(30), CancellationToken.None);
        Assert.Equal(license, duplicate);
        Assert.Single(store.Licenses);
        Assert.Equal(sourceOrderId, duplicate.SourceOrderId);
        var result = await issuer.ValidateAsync(license.LicenseDocument, tenantId, DateTimeOffset.UtcNow, [key.PublicKey], CancellationToken.None);
        Assert.True(result.IsValid, result.Error);
        Assert.Equal("enabled", result.Features!["analytics"]);
        Assert.False((await issuer.ValidateAsync(license.LicenseDocument, Guid.NewGuid(), DateTimeOffset.UtcNow, [key.PublicKey], CancellationToken.None)).IsValid);
        Assert.Contains("<Signature", license.LicenseDocument, StringComparison.Ordinal);
    }

    [Fact]
    public async Task Duplicate_confirmation_with_different_immutable_plan_cannot_silently_reuse_license()
    {
        var (issuer, _, _) = CreateIssuer();
        var subscriptionId = Guid.NewGuid();
        var sourceOrderId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        await issuer.IssueAsync(sourceOrderId, subscriptionId, tenantId, "starter", "v1",
            new Dictionary<string, string> { ["access-points-max"] = "10" }, now, TimeSpan.FromDays(30), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(sourceOrderId, subscriptionId, tenantId, "Business", "v2",
            new Dictionary<string, string> { ["access-points-max"] = "25" }, now, TimeSpan.FromDays(30), CancellationToken.None));
    }

    [Fact]
    public async Task Different_purchase_order_cannot_reuse_a_paid_subscription_license()
    {
        var (issuer, store, _) = CreateIssuer();
        var subscriptionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var features = new Dictionary<string, string> { ["access-points-max"] = "25" };
        var original = await issuer.IssueAsync(Guid.NewGuid(), subscriptionId, tenantId, "Business", "v1",
            features, now, TimeSpan.FromDays(30), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(Guid.NewGuid(), subscriptionId, tenantId,
            "Business", "v1", features, now, TimeSpan.FromDays(30), CancellationToken.None));
        Assert.Equal(original, Assert.Single(store.Licenses));
    }

    [Fact]
    public async Task Cancelled_subscription_cannot_regain_readiness_from_delayed_paid_event_replay()
    {
        var (issuer, store, _) = CreateIssuer();
        var sourceOrderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var features = new Dictionary<string, string> { ["access-points-max"] = "25" };
        var signed = await issuer.IssueAsync(sourceOrderId, subscriptionId, tenantId,
            "Business", "v1", features, now, TimeSpan.FromDays(30), CancellationToken.None);
        await issuer.RevokeAsync(signed.LicenseId, CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(sourceOrderId, subscriptionId,
            tenantId, "Business", "v1", features, now, TimeSpan.FromDays(30), CancellationToken.None));
        Assert.True(Assert.Single(store.Licenses).Revoked);
    }

    [Fact]
    public async Task Suspended_subscription_blocks_late_payment_and_revokes_an_existing_license()
    {
        var (issuer, store, key) = CreateIssuer();
        var orderId = Guid.NewGuid();
        var subscriptionId = Guid.NewGuid();
        var tenantId = Guid.NewGuid();
        var now = DateTimeOffset.UtcNow;
        var features = new Dictionary<string, string> { ["access-points-max"] = "25" };

        await issuer.SuspendAsync(orderId, subscriptionId, tenantId, now, CancellationToken.None);
        await issuer.SuspendAsync(orderId, subscriptionId, tenantId, now, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(orderId, subscriptionId,
            tenantId, "Business", "v1", features, now, TimeSpan.FromDays(30), CancellationToken.None));
        Assert.Empty(store.Licenses);

        var activeSubscription = Guid.NewGuid();
        var active = await issuer.IssueAsync(orderId, activeSubscription, tenantId,
            "Business", "v1", features, now, TimeSpan.FromDays(30), CancellationToken.None);
        await issuer.SuspendAsync(orderId, activeSubscription, tenantId, now, CancellationToken.None);
        Assert.False((await issuer.ValidateAsync(active.LicenseDocument, tenantId, now.AddMinutes(1), [key.PublicKey], CancellationToken.None)).IsValid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(orderId, activeSubscription,
            tenantId, "Business", "v1", features, now, TimeSpan.FromDays(30), CancellationToken.None));
        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.SuspendAsync(Guid.NewGuid(), activeSubscription,
            tenantId, now, CancellationToken.None));
    }

    [Fact]
    public async Task Issue_signs_and_verifies_plan_limit_values_not_only_feature_presence()
    {
        var (issuer, _, key) = CreateIssuer();
        var tenantId = Guid.NewGuid();
        var features = new Dictionary<string, string>
        {
            ["access-points-max"] = "25",
            ["supports-analytics"] = "True",
        };
        var license = await issuer.IssueAsync(Guid.NewGuid(), Guid.NewGuid(), tenantId, "Business", "2026-10",
            features, DateTimeOffset.UtcNow, TimeSpan.FromDays(30), CancellationToken.None);
        var verified = await issuer.ValidateAsync(license.LicenseDocument, tenantId, DateTimeOffset.UtcNow,
            [key.PublicKey], CancellationToken.None);

        Assert.True(verified.IsValid, verified.Error);
        Assert.Equal("25", verified.Features!["access-points-max"]);
        Assert.Equal("True", verified.Features["supports-analytics"]);
    }

    [Fact]
    public async Task Reissue_for_existing_subscription_cannot_return_another_tenants_license()
    {
        var (issuer, _, _) = CreateIssuer();
        var subscriptionId = Guid.NewGuid();
        var firstTenantId = Guid.NewGuid();
        var issued = await issuer.IssueAsync(Guid.NewGuid(), subscriptionId, firstTenantId, "starter", "v1", new Dictionary<string, string>(), DateTimeOffset.UtcNow, TimeSpan.FromDays(30), CancellationToken.None);

        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(Guid.NewGuid(), subscriptionId, Guid.NewGuid(), "starter", "v1", new Dictionary<string, string>(), DateTimeOffset.UtcNow, TimeSpan.FromDays(30), CancellationToken.None));
        Assert.Equal(firstTenantId, issued.TenantId);
    }
    [Fact]
    public async Task Validation_rejects_tampered_expired_or_untrusted_signatures_and_revocation()
    {
        var (issuer, _, key) = CreateIssuer();
        var license = await issuer.IssueAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "pro", "v2", new Dictionary<string, string> { ["reports"] = "enabled" }, DateTimeOffset.UtcNow.AddDays(-2), TimeSpan.FromDays(1), CancellationToken.None);
        Assert.False((await issuer.ValidateAsync(license.LicenseDocument.Replace("v2", "vX", StringComparison.Ordinal), license.TenantId, DateTimeOffset.UtcNow, [key.PublicKey], CancellationToken.None)).IsValid);
        Assert.False((await issuer.ValidateAsync(license.LicenseDocument, license.TenantId, DateTimeOffset.UtcNow.AddDays(1), [key.PublicKey], CancellationToken.None)).IsValid);
        Assert.False((await issuer.ValidateAsync(license.LicenseDocument, license.TenantId, DateTimeOffset.UtcNow, [key.OtherPublicKey], CancellationToken.None)).IsValid);
        await issuer.RevokeAsync(license.LicenseId, CancellationToken.None);
        Assert.False((await issuer.ValidateAsync(license.LicenseDocument, license.TenantId, DateTimeOffset.UtcNow, [key.PublicKey], CancellationToken.None)).IsValid);
    }

    [Fact]
    public async Task Issue_fails_closed_when_secret_is_unavailable()
    {
        var issuer = new LicenseIssuer(new InMemoryLicenseStore(), new FixedKeyProvider(null));
        await Assert.ThrowsAsync<InvalidOperationException>(() => issuer.IssueAsync(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "pro", "v1", new Dictionary<string, string>(), DateTimeOffset.UtcNow, TimeSpan.FromDays(1), CancellationToken.None));
    }

    [Fact]
    public async Task Paid_subscription_event_issues_only_verified_order_bound_license_and_attested_readiness()
    {
        var (issuer, store, signingKey) = CreateIssuer();
        var proofKey = Encoding.UTF8.GetBytes("test-license-proof-key-32-bytes-minimum");
        using var billingPrivateKey = RSA.Create(2048);
        using var billingPublicKeys = new SubscriptionPaymentVerificationKeys([billingPrivateKey.ExportSubjectPublicKeyInfoPem()]);
        var handler = new SubscriptionPaymentConfirmedHandler(issuer,
            new ReadinessSigningKey(Convert.ToBase64String(proofKey)), billingPublicKeys);
        var now = DateTimeOffset.UtcNow;
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SourceOrderId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            TenantIdentifier = "test-business-tenant", PaymentMethod = "captured", PlanId = "Business",
            PlanVersion = "2026-10", AmountMinor = 10000, Currency = "USD",
            DatabaseStrategy = "shared", DatabaseProvider = "postgres", MaxLocations = 2,
            Features = new Dictionary<string, string> { ["access-points-max"] = "25" },
            ValidFromUtc = now, ValidUntilUtc = now.AddDays(30), ConfirmedAtUtc = now,
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(paid, CancellationToken.None));
        Assert.Empty(store.Licenses);
        paid.Proof = SubscriptionPaymentProof.Sign(paid, billingPrivateKey);
        paid.AmountMinor++;
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(paid, CancellationToken.None));
        Assert.Empty(store.Licenses);
        paid.AmountMinor--;
        paid.DatabaseStrategy = "dedicated";
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(paid, CancellationToken.None));
        Assert.Empty(store.Licenses);
        paid.DatabaseStrategy = "shared";
        paid.Currency = new string('X', 4097);
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(paid, CancellationToken.None));
        Assert.Empty(store.Licenses);
        paid.Currency = "USD";

        var ready = await handler.Handle(paid, CancellationToken.None);
        var signed = Assert.Single(store.Licenses);
        Assert.Equal(signed.LicenseId, ready.EvidenceId);
        Assert.Equal(paid.SourceOrderId, signed.SourceOrderId);
        Assert.Equal(paid.SourceOrderId, ready.SourceOrderId);
        Assert.True(TenantReadinessProof.Verify(ready, proofKey));
        Assert.Equal("25", (await issuer.ValidateAsync(signed.LicenseDocument, paid.TenantId, now,
            [signingKey.PublicKey], CancellationToken.None)).Features!["access-points-max"]);

        var replay = await handler.Handle(paid, CancellationToken.None);
        Assert.Equal(ready.EvidenceId, replay.EvidenceId);
        Assert.Single(store.Licenses);
        replay.SourceOrderId = Guid.NewGuid();
        Assert.False(TenantReadinessProof.Verify(replay, proofKey));

        paid.PaymentMethod = "stub";
        await Assert.ThrowsAsync<InvalidOperationException>(() => handler.Handle(paid, CancellationToken.None));
        Assert.Single(store.Licenses);
    }

    [Fact]
    public async Task Only_billing_signed_suspension_revokes_and_blocks_out_of_order_payment_event()
    {
        var (issuer, store, _) = CreateIssuer();
        using var billingPrivateKey = RSA.Create(2048);
        using var billingPublicKeys = new SubscriptionPaymentVerificationKeys([billingPrivateKey.ExportSubjectPublicKeyInfoPem()]);
        var suspensionHandler = new SubscriptionAccessSuspendedHandler(issuer, billingPublicKeys);
        var paidHandler = new SubscriptionPaymentConfirmedHandler(issuer,
            new ReadinessSigningKey(Convert.ToBase64String(Encoding.UTF8.GetBytes("test-license-proof-key-32-bytes-minimum"))), billingPublicKeys);
        var now = DateTimeOffset.UtcNow;
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SourceOrderId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            TenantIdentifier = "test-business-tenant", PaymentMethod = "captured", PlanId = "Business",
            PlanVersion = "2026-10", AmountMinor = 10000, Currency = "USD",
            DatabaseStrategy = "shared", DatabaseProvider = "postgres", MaxLocations = 2,
            Features = new Dictionary<string, string> { ["access-points-max"] = "25" },
            ValidFromUtc = now, ValidUntilUtc = now.AddDays(30), ConfirmedAtUtc = now,
        };
        paid.Proof = SubscriptionPaymentProof.Sign(paid, billingPrivateKey);
        var suspension = new SubscriptionAccessSuspendedIntegrationEvent
        {
            SourceOrderId = paid.SourceOrderId, SubscriptionId = paid.SubscriptionId, TenantId = paid.TenantId,
            TenantIdentifier = paid.TenantIdentifier, Reason = "cancelled", SuspendedAtUtc = now.AddMinutes(1),
        };

        await Assert.ThrowsAsync<InvalidOperationException>(() => suspensionHandler.Handle(suspension, CancellationToken.None));
        await paidHandler.Handle(paid, CancellationToken.None);
        Assert.False(Assert.Single(store.Licenses).Revoked);

        suspension.Proof = SubscriptionSuspensionProof.Sign(suspension, billingPrivateKey);
        await suspensionHandler.Handle(suspension, CancellationToken.None);
        Assert.True(Assert.Single(store.Licenses).Revoked);
        await suspensionHandler.Handle(suspension, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => paidHandler.Handle(paid, CancellationToken.None));

        var delayedPaid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SourceOrderId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            TenantIdentifier = "delayed-business-tenant", PaymentMethod = "captured", PlanId = "Business",
            PlanVersion = "2026-10", AmountMinor = 10000, Currency = "USD",
            DatabaseStrategy = "shared", DatabaseProvider = "postgres", MaxLocations = 2,
            Features = new Dictionary<string, string> { ["access-points-max"] = "25" },
            ValidFromUtc = now, ValidUntilUtc = now.AddDays(30), ConfirmedAtUtc = now,
        };
        delayedPaid.Proof = SubscriptionPaymentProof.Sign(delayedPaid, billingPrivateKey);
        var earlySuspension = new SubscriptionAccessSuspendedIntegrationEvent
        {
            SourceOrderId = delayedPaid.SourceOrderId, SubscriptionId = delayedPaid.SubscriptionId,
            TenantId = delayedPaid.TenantId, TenantIdentifier = delayedPaid.TenantIdentifier,
            Reason = "cancelled", SuspendedAtUtc = now.AddMinutes(1),
        };
        earlySuspension.Proof = SubscriptionSuspensionProof.Sign(earlySuspension, billingPrivateKey);
        await suspensionHandler.Handle(earlySuspension, CancellationToken.None);
        await Assert.ThrowsAsync<InvalidOperationException>(() => paidHandler.Handle(delayedPaid, CancellationToken.None));
        Assert.Single(store.Licenses);
    }

    [Fact]
    public async Task SignedPaidRenewal_IssuesNewSourceOrderAndRetainsPeriodSpecificRevocation()
    {
        var (issuer, store, key) = CreateIssuer();
        using var billingPrivateKey = RSA.Create(2048);
        using var billingKeys = new SubscriptionPaymentVerificationKeys([billingPrivateKey.ExportSubjectPublicKeyInfoPem()]);
        var paidHandler = new SubscriptionPaymentConfirmedHandler(issuer,
            new ReadinessSigningKey(Convert.ToBase64String(Encoding.UTF8.GetBytes("test-license-proof-key-32-bytes-minimum"))), billingKeys);
        var suspendHandler = new SubscriptionAccessSuspendedHandler(issuer, billingKeys);
        var now = DateTimeOffset.UtcNow;
        var first = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SourceOrderId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            TenantIdentifier = "renew-license-tenant", PaymentMethod = "captured", PlanId = "Business",
            PlanVersion = "2026-10", AmountMinor = 14999, Currency = "USD", DatabaseStrategy = "shared",
            DatabaseProvider = "postgres", Features = new Dictionary<string, string> { ["access-points-max"] = "25" },
            ValidFromUtc = now.AddMonths(-11), ValidUntilUtc = now.AddMonths(1), ConfirmedAtUtc = now.AddMonths(-11),
        };
        first.Proof = SubscriptionPaymentProof.Sign(first, billingPrivateKey);
        await paidHandler.Handle(first, CancellationToken.None);
        var previous = Assert.Single(store.Licenses);
        var renewal = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SourceOrderId = Guid.NewGuid(), PreviousSourceOrderId = first.SourceOrderId,
            SubscriptionId = first.SubscriptionId, TenantId = first.TenantId, TenantIdentifier = first.TenantIdentifier,
            PaymentMethod = "captured", PlanId = "Business", PlanVersion = "2026-10", AmountMinor = 14999,
            Currency = "USD", DatabaseStrategy = "shared", DatabaseProvider = "postgres",
            Features = new Dictionary<string, string> { ["access-points-max"] = "25" },
            ConfirmedAtUtc = now, ValidFromUtc = first.ValidUntilUtc, ValidUntilUtc = first.ValidUntilUtc.AddYears(1),
        };
        renewal.Proof = SubscriptionPaymentProof.Sign(renewal, billingPrivateKey);
        var ready = await paidHandler.Handle(renewal, CancellationToken.None);
        Assert.Equal(renewal.SourceOrderId, ready.SourceOrderId);
        var current = store.Licenses.Single(record => !record.Revoked);
        Assert.Equal(renewal.SourceOrderId, current.SourceOrderId);
        Assert.True(store.Licenses.Single(record => record.LicenseId == previous.LicenseId).Revoked);
        Assert.True((await issuer.ValidateAsync(current.LicenseDocument, first.TenantId, now, [key.PublicKey], CancellationToken.None)).IsValid);

        var oldSuspension = new SubscriptionAccessSuspendedIntegrationEvent
        {
            SourceOrderId = first.SourceOrderId, SubscriptionId = first.SubscriptionId, TenantId = first.TenantId,
            TenantIdentifier = first.TenantIdentifier, Reason = "renewal_failed", SuspendedAtUtc = first.ValidUntilUtc,
        };
        oldSuspension.Proof = SubscriptionSuspensionProof.Sign(oldSuspension, billingPrivateKey);
        await suspendHandler.Handle(oldSuspension, CancellationToken.None);
        Assert.True((await issuer.ValidateAsync(current.LicenseDocument, first.TenantId, now, [key.PublicKey], CancellationToken.None)).IsValid);
        Assert.Equal(current.LicenseId, (await paidHandler.Handle(renewal, CancellationToken.None)).EvidenceId);
        renewal.Features["access-points-max"] = "100";
        renewal.Proof = SubscriptionPaymentProof.Sign(renewal, billingPrivateKey);
        await Assert.ThrowsAsync<InvalidOperationException>(() => paidHandler.Handle(renewal, CancellationToken.None));
    }

    [Fact]
    public async Task FreshPaymentAfterFailedRenewal_ReissuesFromRevokedPeriodAndKeepsOldTombstone()
    {
        var (issuer, store, key) = CreateIssuer();
        using var privateKey = RSA.Create(2048);
        using var keys = new SubscriptionPaymentVerificationKeys([privateKey.ExportSubjectPublicKeyInfoPem()]);
        var paidHandler = new SubscriptionPaymentConfirmedHandler(issuer,
            new ReadinessSigningKey(Convert.ToBase64String(Encoding.UTF8.GetBytes("test-license-proof-key-32-bytes-minimum"))), keys);
        var suspendHandler = new SubscriptionAccessSuspendedHandler(issuer, keys);
        var now = DateTimeOffset.UtcNow;
        var first = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SourceOrderId = Guid.NewGuid(), SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(),
            TenantIdentifier = "recovered-license-tenant", PaymentMethod = "captured", PlanId = "Business",
            PlanVersion = "2026-10", AmountMinor = 14999, Currency = "USD", DatabaseStrategy = "shared",
            DatabaseProvider = "postgres", Features = new Dictionary<string, string> { ["access-points-max"] = "25" },
            ValidFromUtc = now.AddYears(-1), ValidUntilUtc = now.AddDays(1),
            ConfirmedAtUtc = now.AddYears(-1),
        };
        first.Proof = SubscriptionPaymentProof.Sign(first, privateKey);
        await paidHandler.Handle(first, CancellationToken.None);
        var oldLicense = Assert.Single(store.Licenses);
        var suspension = new SubscriptionAccessSuspendedIntegrationEvent
        {
            SourceOrderId = first.SourceOrderId, SubscriptionId = first.SubscriptionId, TenantId = first.TenantId,
            TenantIdentifier = first.TenantIdentifier, Reason = "renewal_failed", SuspendedAtUtc = now,
        };
        suspension.Proof = SubscriptionSuspensionProof.Sign(suspension, privateKey);
        await suspendHandler.Handle(suspension, CancellationToken.None);
        Assert.False((await issuer.ValidateAsync(oldLicense.LicenseDocument, first.TenantId, now, [key.PublicKey], CancellationToken.None)).IsValid);
        var recovery = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SourceOrderId = Guid.NewGuid(), PreviousSourceOrderId = first.SourceOrderId,
            SubscriptionId = first.SubscriptionId, TenantId = first.TenantId, TenantIdentifier = first.TenantIdentifier,
            PaymentMethod = "external_invoice", PlanId = "Business", PlanVersion = "2026-10", AmountMinor = 14999,
            Currency = "USD", DatabaseStrategy = "shared", DatabaseProvider = "postgres",
            Features = new Dictionary<string, string> { ["access-points-max"] = "25" },
            ConfirmedAtUtc = now, ValidFromUtc = now, ValidUntilUtc = now.AddYears(1),
        };
        recovery.Proof = SubscriptionPaymentProof.Sign(recovery, privateKey);
        var ready = await paidHandler.Handle(recovery, CancellationToken.None);
        Assert.Equal(recovery.SourceOrderId, ready.SourceOrderId);
        var current = store.Licenses.Single(record => !record.Revoked);
        Assert.NotEqual(oldLicense.LicenseId, current.LicenseId);
        Assert.True((await issuer.ValidateAsync(current.LicenseDocument, first.TenantId, now, [key.PublicKey], CancellationToken.None)).IsValid);
        await Assert.ThrowsAsync<InvalidOperationException>(() => paidHandler.Handle(first, CancellationToken.None));
    }

    private static (LicenseIssuer Issuer, InMemoryLicenseStore Store, (LicenseSigningKey? SigningKey, string PublicKey, string OtherPublicKey) Key) CreateIssuer()
    {
        var pair = KeyGenerator.Create().GenerateKeyPair();
        var other = KeyGenerator.Create().GenerateKeyPair();
        const string passphrase = "test-only-passphrase";
        var key = new LicenseSigningKey("test-key", pair.ToEncryptedPrivateKeyString(passphrase), passphrase);
        var store = new InMemoryLicenseStore();
        return (new LicenseIssuer(store, new FixedKeyProvider(key)), store, (key, pair.ToPublicKeyString(), other.ToPublicKeyString()));
    }

    private sealed class FixedKeyProvider(LicenseSigningKey? key) : ILicenseSigningKeyProvider
    {
        public Task<LicenseSigningKey?> GetCurrentAsync(CancellationToken cancellationToken) => Task.FromResult(key);
    }

    private sealed class InMemoryLicenseStore : ILicenseStore
    {
        public List<IssuedLicense> Licenses { get; } = [];
        private readonly Dictionary<Guid, (Guid SubscriptionId, Guid TenantId)> suspensions = [];
        public Task<bool> IsSuspendedAsync(Guid sourceOrderId, Guid subscriptionId, CancellationToken cancellationToken) =>
            Task.FromResult(suspensions.TryGetValue(sourceOrderId, out var row) && row.SubscriptionId == subscriptionId);
        public Task AddUnlessSuspendedAsync(IssuedLicense license, CancellationToken cancellationToken)
        {
            if (suspensions.ContainsKey(license.SourceOrderId))
            {
                throw new InvalidOperationException("Suspended subscription cannot receive a license.");
            }

            Licenses.Add(license);
            return Task.CompletedTask;
        }

        public Task SuspendAsync(Guid sourceOrderId, Guid subscriptionId, Guid tenantId, DateTimeOffset suspendedAtUtc, CancellationToken cancellationToken)
        {
            if (suspensions.TryGetValue(sourceOrderId, out var prior) && (prior.SubscriptionId != subscriptionId || prior.TenantId != tenantId))
            {
                throw new InvalidOperationException("Suspension identity differs from the confirmed subscription.");
            }

            var active = Licenses.FirstOrDefault(x => x.SubscriptionId == subscriptionId && !x.Revoked);
            if (active is not null && active.TenantId != tenantId)
            {
                throw new InvalidOperationException("Subscription belongs to a different tenant.");
            }
            if (active is not null && active.SourceOrderId != sourceOrderId &&
                !Licenses.Any(x => x.SourceOrderId == sourceOrderId && x.SubscriptionId == subscriptionId && x.Revoked))
            {
                throw new InvalidOperationException("Suspension does not match a known paid period.");
            }

            suspensions[sourceOrderId] = (subscriptionId, tenantId);
            for (var index = 0; index < Licenses.Count; index++)
            {
                if (Licenses[index].SourceOrderId == sourceOrderId && Licenses[index].SubscriptionId == subscriptionId)
                {
                    Licenses[index] = Licenses[index] with { Revoked = true };
                }
            }

            return Task.CompletedTask;
        }
        public Task<IssuedLicense?> FindBySubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken) =>
            Task.FromResult(Licenses.Where(x => x.SubscriptionId == subscriptionId)
                .OrderBy(x => x.Revoked).ThenByDescending(x => x.IssuedAt).FirstOrDefault());
        public Task<IssuedLicense?> FindByLicenseIdAsync(Guid licenseId, CancellationToken cancellationToken) => Task.FromResult(Licenses.FirstOrDefault(x => x.LicenseId == licenseId));
        public Task UpdateAsync(IssuedLicense license, CancellationToken cancellationToken) { var index = Licenses.FindIndex(x => x.LicenseId == license.LicenseId); Licenses[index] = license; return Task.CompletedTask; }
        public Task ReplaceAsync(Guid licenseId, IssuedLicense replacement, CancellationToken cancellationToken)
        {
            if (suspensions.ContainsKey(replacement.SourceOrderId))
                throw new InvalidOperationException("Suspended paid order cannot receive a replacement license.");
            var index = Licenses.FindIndex(x => x.LicenseId == licenseId);
            Licenses[index] = Licenses[index] with { Revoked = true };
            Licenses.Add(replacement);
            return Task.CompletedTask;
        }
    }
}

