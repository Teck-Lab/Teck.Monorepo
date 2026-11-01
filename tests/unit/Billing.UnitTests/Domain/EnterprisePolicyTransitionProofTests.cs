using System.Security.Cryptography;
using SharedKernel.Events;
using Xunit;

namespace Billing.UnitTests.Domain;

public sealed class EnterprisePolicyTransitionProofTests
{
    [Fact]
    public void Verify_AcceptsSignedTransitionAndRejectsChangedBindingOrTerms()
    {
        using var key = RSA.Create(2048);
        var transition = new EnterprisePolicyAttachedIntegrationEvent
        {
            TenantId = Guid.NewGuid(), TenantIdentifier = "acme", SubscriptionId = Guid.NewGuid(), SourceOrderId = Guid.NewGuid(),
            PolicyId = "enterprise", PolicyVersion = "v1", GracePeriodDays = 20, BackupRetentionDays = 365,
            LicenseKind = "custom_enterprise",
            LicensingSigningKeyId = "licensing-key", LicensingSignature = "signed-document",
            ValidFromUtc = DateTimeOffset.UtcNow, ValidUntilUtc = DateTimeOffset.UtcNow.AddYears(1),
        };
        var tenantId = transition.TenantId;
        transition.Proof = EnterprisePolicyAttachedProof.Sign(transition, key);
        Assert.True(EnterprisePolicyAttachedProof.Verify(transition, key));
        transition.TenantId = Guid.NewGuid();
        Assert.False(EnterprisePolicyAttachedProof.Verify(transition, key));
        transition.TenantId = tenantId;
        transition.GracePeriodDays++;
        Assert.False(EnterprisePolicyAttachedProof.Verify(transition, key));
    }
}
