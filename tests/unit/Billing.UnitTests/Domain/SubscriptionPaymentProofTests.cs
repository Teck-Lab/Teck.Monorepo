using System.Security.Cryptography;
using SharedKernel.Events;
using Xunit;

namespace Billing.UnitTests.Domain;

public sealed class SubscriptionPaymentProofTests
{
    [Fact]
    public void Verify_RejectsChangesToIndependentlySignedAddOnSelections()
    {
        using var key = RSA.Create(2048);
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SubscriptionId = Guid.NewGuid(),
            TenantId = Guid.NewGuid(),
            TenantIdentifier = "acme",
            SourceOrderId = Guid.NewGuid(),
            PlanId = "Business",
            PlanVersion = "2026-10",
            AmountMinor = 20999,
            Currency = "USD",
            DatabaseStrategy = "dedicated",
            DatabaseProvider = "postgres",
            HasScheduledBackups = true,
            HasHighAvailability = true,
            HasReadReplicas = true,
            ReplicaCount = 2,
            PaymentMethod = "captured",
            ConfirmedAtUtc = DateTimeOffset.UtcNow,
            ValidFromUtc = DateTimeOffset.UtcNow,
            ValidUntilUtc = DateTimeOffset.UtcNow.AddYears(1),
            PolicyVersion = 4,
        };
        paid.Proof = SubscriptionPaymentProof.Sign(paid, key);

        Assert.True(SubscriptionPaymentProof.Verify(paid, key));
        paid.HasScheduledBackups = false;
        Assert.False(SubscriptionPaymentProof.Verify(paid, key));
        paid.HasScheduledBackups = true;
        paid.HasHighAvailability = false;
        Assert.False(SubscriptionPaymentProof.Verify(paid, key));
    }

    [Fact]
    public void Verify_VersionedSnapshotAuthenticatesEffectivePolicyAndDefaults()
    {
        using var key = RSA.Create(2048);
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(), TenantIdentifier = "acme",
            SourceOrderId = Guid.NewGuid(), PlanId = "Business", PlanVersion = "2026-10",
            AmountMinor = 1, Currency = "USD", DatabaseStrategy = "dedicated", DatabaseProvider = "postgres",
            PaymentMethod = "captured", ConfirmedAtUtc = DateTimeOffset.UtcNow,
            ValidFromUtc = DateTimeOffset.UtcNow, ValidUntilUtc = DateTimeOffset.UtcNow.AddYears(1),
            PolicyVersion = 4, EffectiveGracePeriodDays = 7, EffectiveBackupRetentionDays = 30,
        };
        paid.Proof = SubscriptionPaymentProof.Sign(paid, key);

        Assert.True(SubscriptionPaymentProof.Verify(paid, key));
        paid.EffectiveGracePeriodDays = 8;
        Assert.False(SubscriptionPaymentProof.Verify(paid, key));
    }

    [Fact]
    public void Verify_VersionedSnapshotAcceptsSignedCustomPolicyIdentity()
    {
        using var key = RSA.Create(2048);
        var paid = new SubscriptionPaymentConfirmedIntegrationEvent
        {
            SubscriptionId = Guid.NewGuid(), TenantId = Guid.NewGuid(), TenantIdentifier = "acme",
            SourceOrderId = Guid.NewGuid(), PlanId = "Business", PlanVersion = "2026-10",
            AmountMinor = 1, Currency = "USD", DatabaseStrategy = "dedicated", DatabaseProvider = "postgres",
            PaymentMethod = "captured", ConfirmedAtUtc = DateTimeOffset.UtcNow,
            ValidFromUtc = DateTimeOffset.UtcNow, ValidUntilUtc = DateTimeOffset.UtcNow.AddYears(1),
            PolicyVersion = 4, EffectiveGracePeriodDays = 42, EffectiveBackupRetentionDays = 91,
            EnterprisePolicyId = "enterprise-acme", EnterprisePolicyVersion = "2",
        };
        paid.Proof = SubscriptionPaymentProof.Sign(paid, key);
        Assert.True(SubscriptionPaymentProof.Verify(paid, key));
        paid.EnterprisePolicyVersion = "1";
        Assert.False(SubscriptionPaymentProof.Verify(paid, key));
    }
}
