using System.Security.Cryptography;
using MemoryPack;
using SharedKernel.Events;
using Xunit;

namespace SharedKernel.UnitTests.Events;

public sealed class TenantPurgeApprovalProofTests
{
    [Fact]
    public void SignVerify_RejectsWrongTenantSourceOrderAndSigner()
    {
        using var signer = RSA.Create(2048);
        using var verifier = RSA.Create();
        verifier.ImportFromPem(signer.ExportSubjectPublicKeyInfoPem());
        using var stranger = RSA.Create(2048);
        var suspendedAt = new DateTimeOffset(2026, 10, 1, 0, 0, 0, TimeSpan.Zero);
        var eventToApprove = new TenantPurgeApprovedIntegrationEvent
        {
            ApprovalId = Guid.NewGuid(), TenantId = Guid.NewGuid(), TenantIdentifier = "paid-tenant",
            SubscriptionId = Guid.NewGuid(), SourceOrderId = Guid.NewGuid(),
            FirstApproverSubjectId = "manager-one", SecondApproverSubjectId = "manager-two",
            SuspendedAtUtc = suspendedAt, EligibleAtUtc = suspendedAt.AddDays(30), ApprovedAtUtc = suspendedAt.AddDays(31),
        };
        eventToApprove.Proof = TenantPurgeApprovalProof.Sign(eventToApprove, signer);

        Assert.True(TenantPurgeApprovalProof.Verify(eventToApprove, verifier));
        var correctTenantId = eventToApprove.TenantId;
        eventToApprove.TenantId = Guid.NewGuid();
        Assert.False(TenantPurgeApprovalProof.Verify(eventToApprove, verifier));
        eventToApprove.TenantId = correctTenantId;
        var correctOrderId = eventToApprove.SourceOrderId;
        eventToApprove.SourceOrderId = Guid.NewGuid();
        Assert.False(TenantPurgeApprovalProof.Verify(eventToApprove, verifier));
        eventToApprove.SourceOrderId = correctOrderId;
        eventToApprove.EligibleAtUtc = suspendedAt;
        Assert.False(TenantPurgeApprovalProof.Verify(eventToApprove, verifier));
        eventToApprove.EligibleAtUtc = suspendedAt.AddDays(30);
        Assert.False(TenantPurgeApprovalProof.Verify(eventToApprove, stranger));

        var restored = MemoryPackSerializer.Deserialize<TenantPurgeApprovedIntegrationEvent>(
            MemoryPackSerializer.Serialize(eventToApprove));
        Assert.NotNull(restored);
        Assert.True(TenantPurgeApprovalProof.Verify(restored, verifier));
    }

    [Fact]
    public void Sign_RejectsSameApproverAndMissingPaidIdentity()
    {
        using var signer = RSA.Create(2048);
        var suspendedAt = DateTimeOffset.UtcNow.AddDays(-31);
        var request = new TenantPurgeApprovedIntegrationEvent
        {
            ApprovalId = Guid.NewGuid(), TenantId = Guid.NewGuid(), TenantIdentifier = "paid-tenant",
            SubscriptionId = Guid.NewGuid(), SourceOrderId = Guid.NewGuid(),
            FirstApproverSubjectId = "manager-one", SecondApproverSubjectId = "manager-one",
            SuspendedAtUtc = suspendedAt, EligibleAtUtc = suspendedAt.AddDays(30), ApprovedAtUtc = DateTimeOffset.UtcNow,
        };
        Assert.Throws<ArgumentException>(() => TenantPurgeApprovalProof.Sign(request, signer));
        request.SecondApproverSubjectId = "manager-two";
        request.SourceOrderId = Guid.Empty;
        Assert.Throws<ArgumentException>(() => TenantPurgeApprovalProof.Sign(request, signer));
    }
}
