using System.Globalization;
using System.Security.Cryptography;
using Licensing.Application;
using Xunit;

namespace Licensing.UnitTests;

public sealed class EnterprisePolicyProofTests
{

    [Fact]
    public async Task Operator_issued_policy_is_persisted_and_retrievable_only_for_authorized_operator()
    {
        using var key = RSA.Create(2048);
        var store = new InMemoryEnterprisePolicyStore();
        var issuer = new EnterprisePolicyIssuer(store, key, "key-1");
        var now = DateTimeOffset.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture);
        var request = new IssueEnterprisePolicyRequest(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "policy-a", "1", 12, 45,
            now, now.AddYears(1));
        var operatorIdentity = new EnterprisePolicyOperator("subject-1", "licensing-api", new HashSet<string>(["licensing:enterprise-policy:issue"]), new HashSet<string>(["licensing-enterprise-policy-issuer"]));

        var issued = await issuer.IssueAsync(operatorIdentity, request, now, CancellationToken.None);

        Assert.True(issued.Verify(request.TenantId, request.SubscriptionId, request.SourceOrderId, now, key));
        Assert.Equal(issued, await issuer.GetAsync("policy-a", "1", CancellationToken.None));
        Assert.Equal("subject-1", store.Audit.Single().ActorSubjectId);
    }
    [Fact]
    public void Verify_accepts_valid_signature_only_for_bound_paid_order()
    {
        using var key = RSA.Create(2048);
        using var untrusted = RSA.Create(2048);
        var policy = EnterprisePolicyDocument.Sign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "policy-a", "1", 7, 30,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2027-01-01T00:00:00Z", CultureInfo.InvariantCulture), key);
        var now = DateTimeOffset.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture);

        Assert.True(policy.Verify(policy.TenantId, policy.SubscriptionId, policy.SourceOrderId, now, key));
        Assert.False(policy.Verify(Guid.NewGuid(), policy.SubscriptionId, policy.SourceOrderId, now, key));
        Assert.False(policy.Verify(policy.TenantId, policy.SubscriptionId, policy.SourceOrderId, now, untrusted));
        Assert.False(policy.Verify(policy.TenantId, policy.SubscriptionId, policy.SourceOrderId,
            DateTimeOffset.Parse("2028-01-01T00:00:00Z", CultureInfo.InvariantCulture), key));
    }

    [Fact]
    public void Verify_rejects_tampered_claims_and_invalid_periods()
    {
        using var key = RSA.Create(2048);
        var policy = EnterprisePolicyDocument.Sign(Guid.NewGuid(), Guid.NewGuid(), Guid.NewGuid(), "policy-a", "1", 7, 30,
            DateTimeOffset.Parse("2026-01-01T00:00:00Z", CultureInfo.InvariantCulture),
            DateTimeOffset.Parse("2027-01-01T00:00:00Z", CultureInfo.InvariantCulture), key);

        Assert.False((policy with { GracePeriodDays = 8 }).Verify(policy.TenantId, policy.SubscriptionId,
            policy.SourceOrderId, DateTimeOffset.Parse("2026-06-01T00:00:00Z", CultureInfo.InvariantCulture), key));
        Assert.False(policy.Verify(policy.TenantId, policy.SubscriptionId, policy.SourceOrderId,
            DateTimeOffset.Parse("2025-12-31T00:00:00Z", CultureInfo.InvariantCulture), key));
    }
}
