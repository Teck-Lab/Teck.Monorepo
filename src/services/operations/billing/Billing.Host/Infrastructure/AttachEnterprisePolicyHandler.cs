using Billings.Application.Billing.Purchases;
using Billings.Domain.Entities;
using Licensing.Host.Grpc.V1;
using SharedKernel.Core.Database;
using SharedKernel.Events;
using Wolverine;
using Wolverine.Attributes;

namespace Billings.Host.Infrastructure;

/// <summary>Attaches a verified policy to the paid order and publishes a Billing-attested transition.</summary>
/// <param name="orders">Purchase-order write repository.</param>
/// <param name="unitOfWork">Billing transactional persistence boundary.</param>
/// <param name="tenantLock">Tenant purchase serialization lock.</param>
/// <param name="licensing">Licensing policy lookup.</param>
/// <param name="verificationKeys">Trusted Licensing public keys.</param>
/// <param name="transitionSigner">Billing policy-transition signing key.</param>
/// <param name="bus">Transactional message bus.</param>
public sealed class AttachEnterprisePolicyHandler(
    IGenericWriteRepository<TenantPurchaseOrder, Guid> orders,
    IUnitOfWork unitOfWork,
    IPurchaseTenantLock tenantLock,
    EnterprisePolicyLookupClient licensing,
    EnterprisePolicyVerificationKeys verificationKeys,
    SubscriptionPaymentSigningKey transitionSigner,
    IMessageBus bus)
{
    /// <summary>Attaches active policy terms to the current paid order.</summary>
    /// <param name="command">Requested operator-issued policy and paid order.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>Whether the policy was attached.</returns>
    [Transactional]
    public async Task<bool> Handle(AttachEnterprisePolicyCommand command, CancellationToken cancellationToken)
    {
        if (command.OrderId == Guid.Empty || string.IsNullOrWhiteSpace(command.PolicyId) ||
            string.IsNullOrWhiteSpace(command.PolicyVersion) || string.IsNullOrWhiteSpace(command.RequestedBySubjectId))
        {
            return false;
        }

        var candidate = await orders.FindOneAsync(order => order.Id == command.OrderId, false, cancellationToken).ConfigureAwait(false);
        if (candidate is null || candidate.Status != "active" || candidate.ConfirmedAtUtc is null ||
            !await tenantLock.AcquireAsync(candidate.TenantId, cancellationToken).ConfigureAwait(false))
        {
            return false;
        }

        var order = await orders.FindOneAsync(item => item.Id == command.OrderId, true, cancellationToken).ConfigureAwait(false);
        if (order is null || order.Status != "active" || order.ConfirmedAtUtc is null || order.EnterprisePolicyId is not null)
        {
            return false;
        }

        var response = await licensing.GetAsync(command.PolicyId, command.PolicyVersion, order.TenantId, order.SubscriptionId, order.Id, cancellationToken).ConfigureAwait(false);
        if (response.Status != PolicyStatus.Active || response.Policy is null || response.Policy.PolicyId != command.PolicyId ||
            response.Policy.PolicyVersion != command.PolicyVersion ||
            !verificationKeys.Verify(response.Policy, order.TenantId, order.SubscriptionId, order.Id, DateTimeOffset.UtcNow))
        {
            return false;
        }

        order.AttachEnterprisePolicy(command.PolicyId, command.PolicyVersion, response.Policy.GracePeriodDays, response.Policy.BackupRetentionDays);
        var transition = new EnterprisePolicyAttachedIntegrationEvent
        {
            TenantId = order.TenantId, TenantIdentifier = order.TenantIdentifier, SubscriptionId = order.SubscriptionId,
            SourceOrderId = order.Id, PolicyId = command.PolicyId, PolicyVersion = command.PolicyVersion,
            GracePeriodDays = response.Policy.GracePeriodDays, BackupRetentionDays = response.Policy.BackupRetentionDays,
            LicensingSigningKeyId = response.Policy.SigningKeyId, LicensingSignature = response.Policy.Signature,
            LicenseKind = response.Policy.LicenseKind,
            ValidFromUtc = DateTimeOffset.Parse(response.Policy.ValidFromUtc, System.Globalization.CultureInfo.InvariantCulture),
            ValidUntilUtc = DateTimeOffset.Parse(response.Policy.ValidUntilUtc, System.Globalization.CultureInfo.InvariantCulture),
        };
        transition.Proof = transitionSigner.Sign(transition);
        await bus.PublishAsync(transition).ConfigureAwait(false);
        await unitOfWork.SaveChangesAsync(cancellationToken).ConfigureAwait(false);
        return true;
    }
}
