using Licensing.Host.Grpc.V1;
using SharedKernel.Events;

namespace Customers.Host.Infrastructure;
/// <summary>Requires an attached enterprise policy to remain active in Licensing.</summary>
/// <param name="client">Licensing gRPC client.</param>
/// <param name="verificationKeys">Trusted policy signing keys.</param>
public sealed class EnterprisePolicyFreshnessVerifier(
    EnterprisePolicyLookup.EnterprisePolicyLookupClient client,
    EnterprisePolicyDocumentVerificationKeys verificationKeys) : IEnterprisePolicyFreshnessVerifier
{
    /// <inheritdoc />
    public async Task VerifyAsync(EnterprisePolicyAttachedIntegrationEvent transition, CancellationToken cancellationToken)
    {
        var response = await client.GetEnterprisePolicyAsync(new GetEnterprisePolicyRequest
        {
            PolicyId = transition.PolicyId,
            PolicyVersion = transition.PolicyVersion,
            TenantId = transition.TenantId.ToString("D"),
            SubscriptionId = transition.SubscriptionId.ToString("D"),
            SourceOrderId = transition.SourceOrderId.ToString("D"),
        }, cancellationToken: cancellationToken).ResponseAsync.ConfigureAwait(false);
        var policy = response.Policy;
        if (response.Status != PolicyStatus.Active || policy is null ||
            policy.PolicyId != transition.PolicyId || policy.PolicyVersion != transition.PolicyVersion ||
            policy.TenantId != transition.TenantId.ToString("D") || policy.SubscriptionId != transition.SubscriptionId.ToString("D") ||
            policy.SourceOrderId != transition.SourceOrderId.ToString("D") ||
            policy.GracePeriodDays != transition.GracePeriodDays || policy.BackupRetentionDays != transition.BackupRetentionDays ||
            policy.LicenseKind != transition.LicenseKind || policy.SigningKeyId != transition.LicensingSigningKeyId ||
            policy.Signature != transition.LicensingSignature ||
            !DateTimeOffset.TryParse(policy.ValidFromUtc, out var validFrom) || validFrom != transition.ValidFromUtc ||
            !DateTimeOffset.TryParse(policy.ValidUntilUtc, out var validUntil) || validUntil != transition.ValidUntilUtc ||
            !verificationKeys.Verify(transition, DateTimeOffset.UtcNow))
        {
            throw new InvalidOperationException("Licensing policy is not active or does not match the signed transition.");
        }
    }
}
