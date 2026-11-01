using Grpc.Net.Client;
using Licensing.Host.Grpc.V1;
using Microsoft.Extensions.Options;
using SharedKernel.Events;
namespace Teck.TenantDatabase.Reconciler;

/// <summary>Verifies current Licensing status for an attached policy.</summary>
/// <param name="options">Licensing service discovery address.</param>
/// <param name="verificationKeys">Trusted policy signing keys.</param>
public sealed class EnterprisePolicyFreshnessVerifier(
    IOptions<EnterprisePolicyOptions> options,
    EnterprisePolicyDocumentVerificationKeys verificationKeys) : IEnterprisePolicyFreshnessVerifier
{
    private readonly EnterprisePolicyLookup.EnterprisePolicyLookupClient client =
        new(GrpcChannel.ForAddress(options.Value.Address));

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
            throw new InvalidOperationException("Licensing policy is not active or does not match the signed transition.");
    }
}

/// <summary>Licensing internal gRPC service discovery options.</summary>
public sealed class EnterprisePolicyOptions
{
    /// <summary>Configuration section.</summary>
    public const string SectionName = "EnterprisePolicy";
    /// <summary>Licensing service discovery address.</summary>
    public string Address { get; set; } = "http://licensing:8080";
}
