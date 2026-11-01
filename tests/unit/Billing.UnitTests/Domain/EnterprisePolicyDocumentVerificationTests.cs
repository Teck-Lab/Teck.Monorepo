using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using SharedKernel.Events;
using Xunit;

namespace Billing.UnitTests.Domain;

public sealed class EnterprisePolicyDocumentVerificationTests
{
    [Fact]
    public void Verify_AcceptsExactSignedDocumentAndRejectsChangedOrExpiredClaims()
    {
        using var key = RSA.Create(2048);
        using var verifier = new EnterprisePolicyDocumentVerificationKeys([new KeyValuePair<string, string?>("key-1", key.ExportSubjectPublicKeyInfoPem())]);
        var now = DateTimeOffset.UtcNow;
        var transition = new EnterprisePolicyAttachedIntegrationEvent
        {
            TenantId = Guid.NewGuid(), TenantIdentifier = "acme", SubscriptionId = Guid.NewGuid(), SourceOrderId = Guid.NewGuid(),
            PolicyId = "policy", PolicyVersion = "v1", GracePeriodDays = 30, BackupRetentionDays = 90,
            LicenseKind = "custom_enterprise", LicensingSigningKeyId = "key-1", ValidFromUtc = now.AddMinutes(-1), ValidUntilUtc = now.AddDays(30),
        };
        var payload = JsonSerializer.SerializeToUtf8Bytes(new
        {
            policy_id = transition.PolicyId, policy_version = transition.PolicyVersion, tenant_id = transition.TenantId,
            subscription_id = transition.SubscriptionId, source_order_id = transition.SourceOrderId,
            grace_period_days = transition.GracePeriodDays, backup_retention_days = transition.BackupRetentionDays,
            valid_from_utc = transition.ValidFromUtc.ToUniversalTime().ToString("O"),
            valid_until_utc = transition.ValidUntilUtc.ToUniversalTime().ToString("O"),
            license_kind = transition.LicenseKind, signing_key_id = transition.LicensingSigningKeyId,
        });
        transition.LicensingSignature = Convert.ToBase64String(key.SignData(payload, HashAlgorithmName.SHA256, RSASignaturePadding.Pss));

        Assert.True(verifier.Verify(transition, now));
        transition.GracePeriodDays++;
        Assert.False(verifier.Verify(transition, now));
        transition.GracePeriodDays--;
        Assert.False(verifier.Verify(transition, transition.ValidUntilUtc));
    }
}
