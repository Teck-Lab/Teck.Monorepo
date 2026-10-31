using Standard.Licensing;
using Standard.Licensing.Validation;

namespace Licensing.Application;

/// <summary>Issues and validates signed tenant license documents.</summary>
/// <param name="store">The persistent license store.</param>
/// <param name="keys">The active private signing key provider.</param>
public sealed class LicenseIssuer(ILicenseStore store, ILicenseSigningKeyProvider keys)
{
    /// <summary>Issues a license from an already-authoritative subscription confirmation.</summary>
    /// <param name="sourceOrderId">The confirmed originating purchase order.</param>
    /// <param name="subscriptionId">The confirmed subscription identifier.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="planId">The plan identifier.</param>
    /// <param name="planVersion">The immutable plan version.</param>
    /// <param name="features">The immutable signed feature key/value snapshot.</param>
    /// <param name="now">The issuance time.</param>
    /// <param name="validity">The positive license duration.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The persisted, signed license.</returns>
    public async Task<IssuedLicense> IssueAsync(Guid sourceOrderId, Guid subscriptionId, Guid tenantId, string planId, string planVersion, IReadOnlyDictionary<string, string> features, DateTimeOffset now, TimeSpan validity, CancellationToken cancellationToken)
    {
        ValidateRequest(subscriptionId, tenantId, planId, planVersion, validity);
        if (sourceOrderId == Guid.Empty)
        {
            throw new ArgumentException("Confirmed purchase order is required.", nameof(sourceOrderId));
        }

        ValidateFeatures(features);
        if (await store.IsSuspendedAsync(sourceOrderId, subscriptionId, cancellationToken))
        {
            throw new InvalidOperationException("Suspended subscription cannot receive a license.");
        }

        var prior = await store.FindBySubscriptionAsync(subscriptionId, cancellationToken);
        if (prior is not null)
        {
            if (prior.Revoked)
            {
                throw new InvalidOperationException("Revoked subscription cannot be activated by an earlier payment event.");
            }

            if (prior.TenantId != tenantId)
            {
                throw new InvalidOperationException("Subscription belongs to a different tenant.");
            }

            if (prior.SourceOrderId != sourceOrderId)
            {
                throw new InvalidOperationException("Subscription belongs to a different purchase order.");
            }

            var signed = License.Load(prior.LicenseDocument).ProductFeatures.GetAll()
                .Where(feature => feature.Key is not ("source_order_id" or "subscription_id" or "tenant_id" or "plan_id" or "plan_version"))
                .ToDictionary(feature => feature.Key, feature => feature.Value, StringComparer.Ordinal);
            if (prior.PlanId != planId || prior.PlanVersion != planVersion ||
                signed.Count != features.Count ||
                features.Any(feature => !signed.TryGetValue(feature.Key, out var value) || value != feature.Value))
            {
                throw new InvalidOperationException("Subscription license snapshot differs from the confirmed purchase.");
            }

            return prior;
        }

        var issued = await CreateLicenseAsync(sourceOrderId, subscriptionId, tenantId, planId, planVersion, features, now, validity, cancellationToken);
        await store.AddUnlessSuspendedAsync(issued, cancellationToken);
        return issued;
    }

    /// <summary>Replaces the current signed license after Billing attests a distinct paid successor order.</summary>
    /// <param name="previousOrderId">The last licensed paid order.</param>
    /// <param name="sourceOrderId">The newly paid successor order.</param>
    /// <param name="subscriptionId">The stable subscription identifier.</param>
    /// <param name="tenantId">The tenant owning the subscription.</param>
    /// <param name="planId">The purchased plan.</param>
    /// <param name="planVersion">The immutable plan version.</param>
    /// <param name="features">The confirmed feature snapshot.</param>
    /// <param name="issuedAtUtc">The renewal issue time.</param>
    /// <param name="validUntilUtc">The new paid period expiry.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>The signed license for this paid successor.</returns>
    public async Task<IssuedLicense> IssuePaidRenewalAsync(Guid previousOrderId, Guid sourceOrderId,
        Guid subscriptionId, Guid tenantId, string planId, string planVersion,
        IReadOnlyDictionary<string, string> features, DateTimeOffset issuedAtUtc,
        DateTimeOffset validUntilUtc, CancellationToken cancellationToken)
    {
        ValidateRequest(subscriptionId, tenantId, planId, planVersion, validUntilUtc - issuedAtUtc);
        ValidateFeatures(features);
        if (previousOrderId == Guid.Empty || sourceOrderId == Guid.Empty || sourceOrderId == previousOrderId ||
            await store.IsSuspendedAsync(sourceOrderId, subscriptionId, cancellationToken))
        {
            throw new InvalidOperationException("A distinct, nonsuspended paid renewal order is required.");
        }

        var prior = await store.FindBySubscriptionAsync(subscriptionId, cancellationToken)
            ?? throw new InvalidOperationException("Cannot renew an unknown subscription license.");
        if (prior.TenantId != tenantId)
        {
            throw new InvalidOperationException("Subscription belongs to a different tenant.");
        }

        if (prior.SourceOrderId == sourceOrderId)
        {
            var signed = License.Load(prior.LicenseDocument).ProductFeatures.GetAll()
                .Where(feature => feature.Key is not ("source_order_id" or "subscription_id" or "tenant_id" or "plan_id" or "plan_version"))
                .ToDictionary(feature => feature.Key, feature => feature.Value, StringComparer.Ordinal);
            if (prior.Revoked || prior.ExpiresAt.UtcTicks / 10 != validUntilUtc.UtcTicks / 10 ||
                prior.PlanId != planId || prior.PlanVersion != planVersion || signed.Count != features.Count ||
                features.Any(feature => !signed.TryGetValue(feature.Key, out var value) || value != feature.Value))
            {
                throw new InvalidOperationException("Paid order license snapshot differs from its previous issuance.");
            }

            return prior;
        }

        if (prior.SourceOrderId != previousOrderId)
        {
            throw new InvalidOperationException("Paid renewal does not follow the latest licensed order.");
        }

        var renewed = await CreateLicenseAsync(sourceOrderId, subscriptionId, tenantId, planId, planVersion,
            features, issuedAtUtc, validUntilUtc - issuedAtUtc, cancellationToken);
        await store.ReplaceAsync(prior.LicenseId, renewed, cancellationToken);
        return renewed;
    }

    /// <summary>Revokes an active license and prevents later payment replay from issuing one.</summary>
    /// <param name="sourceOrderId">The originating Billing order.</param>
    /// <param name="subscriptionId">The suspended subscription.</param>
    /// <param name="tenantId">The owning Customer tenant.</param>
    /// <param name="suspendedAtUtc">The effective suspension time.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A task for the revocation operation.</returns>
    public Task SuspendAsync(Guid sourceOrderId, Guid subscriptionId, Guid tenantId, DateTimeOffset suspendedAtUtc, CancellationToken cancellationToken)
    {
        if (sourceOrderId == Guid.Empty || subscriptionId == Guid.Empty || tenantId == Guid.Empty || suspendedAtUtc == default)
        {
            throw new ArgumentException("A paid subscription identity and suspension time are required.");
        }

        return store.SuspendAsync(sourceOrderId, subscriptionId, tenantId, suspendedAtUtc, cancellationToken);
    }

    /// <summary>Revokes a license document.</summary>
    /// <param name="licenseId">The license to revoke.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task for the persistence operation.</returns>
    public async Task RevokeAsync(Guid licenseId, CancellationToken cancellationToken)
    {
        var record = await store.FindByLicenseIdAsync(licenseId, cancellationToken)
            ?? throw new InvalidOperationException("License does not exist.");
        if (!record.Revoked)
        {
            await store.UpdateAsync(record with { Revoked = true }, cancellationToken);
        }
    }

    /// <summary>Checks persistent revocation, signed claims, expiry, and consumer tenant binding.</summary>
    /// <param name="document">The signed license document.</param>
    /// <param name="tenantId">The authenticated consumer tenant.</param>
    /// <param name="now">The current time.</param>
    /// <param name="trustedPublicKeys">The trusted public signing keys.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The validation result.</returns>
    public async Task<LicenseValidationResult> ValidateAsync(string document, Guid tenantId, DateTimeOffset now, IEnumerable<string> trustedPublicKeys, CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(document) || tenantId == Guid.Empty)
        {
            return LicenseValidationResult.Invalid("License and tenant are required.");
        }

        License license;
        try
        {
            license = License.Load(document);
        }
        catch (Exception exception) when (exception is not OperationCanceledException)
        {
            return LicenseValidationResult.Invalid("License document is malformed.");
        }

        if (license.Id == Guid.Empty)
        {
            return LicenseValidationResult.Invalid("License identifier is missing.");
        }

        var record = await store.FindByLicenseIdAsync(license.Id, cancellationToken);
        if (record is null || record.Revoked)
        {
            return LicenseValidationResult.Invalid("License is unknown or revoked.");
        }

        if (record.TenantId != tenantId)
        {
            return LicenseValidationResult.Invalid("License belongs to a different tenant.");
        }

        if (record.ExpiresAt <= now)
        {
            return LicenseValidationResult.Invalid("License is expired.");
        }

        if (!string.Equals(record.LicenseDocument, document, StringComparison.Ordinal))
        {
            return LicenseValidationResult.Invalid("License document does not match the persisted signed license.");
        }

        var validSignature = false;
        foreach (var publicKey in trustedPublicKeys)
        {
            try
            {
                license.Validate().Signature(publicKey).And().ExpirationDate(now.UtcDateTime).AssertValidLicense();
                validSignature = true;
                break;
            }
            catch (Exception exception) when (exception is not OperationCanceledException)
            {
                // Try the next trusted public key.
            }
        }

        if (!validSignature)
        {
            return LicenseValidationResult.Invalid("License signature is invalid or expired.");
        }

        var features = license.ProductFeatures.GetAll()
            .Where(feature => feature.Key is not ("source_order_id" or "subscription_id" or "tenant_id" or "plan_id" or "plan_version"))
            .ToDictionary(feature => feature.Key, feature => feature.Value, StringComparer.Ordinal);
        return LicenseValidationResult.Valid(record, features);
    }

    private static void ValidateFeatures(IReadOnlyDictionary<string, string> features)
    {
        ArgumentNullException.ThrowIfNull(features);
        if (features.Any(feature => string.IsNullOrWhiteSpace(feature.Key) ||
            string.IsNullOrWhiteSpace(feature.Value) ||
            feature.Key is "source_order_id" or "subscription_id" or "tenant_id" or "plan_id" or "plan_version"))
        {
            throw new ArgumentException("License features cannot override identity or plan claims.", nameof(features));
        }
    }

    private static void ValidateRequest(Guid subscriptionId, Guid tenantId, string planId, string planVersion, TimeSpan validity)
    {
        if (subscriptionId == Guid.Empty || tenantId == Guid.Empty)
        {
            throw new ArgumentException("Subscription and tenant identifiers are required.");
        }

        if (string.IsNullOrWhiteSpace(planId) || string.IsNullOrWhiteSpace(planVersion))
        {
            throw new ArgumentException("Plan identity is required.");
        }

        if (validity <= TimeSpan.Zero)
        {
            throw new ArgumentOutOfRangeException(nameof(validity));
        }
    }

    private static string CreateDocument(Guid licenseId, Guid sourceOrderId, Guid subscriptionId, Guid tenantId, string planId, string planVersion, IReadOnlyDictionary<string, string> features, DateTimeOffset expiresAt, LicenseSigningKey key)
    {
        var builder = Standard.Licensing.License.New()
            .WithUniqueIdentifier(licenseId)
            .As(LicenseType.Standard)
            .ExpiresAt(expiresAt.UtcDateTime)
            .WithProductFeatures(product =>
            {
                product.Add("source_order_id", sourceOrderId.ToString("D"));
                product.Add("subscription_id", subscriptionId.ToString("D"));
                product.Add("tenant_id", tenantId.ToString("D"));
                product.Add("plan_id", planId);
                product.Add("plan_version", planVersion);
                foreach (var feature in features)
                {
                    product.Add(feature.Key, feature.Value);
                }
            })
            .WithAdditionalAttributes(new Dictionary<string, string> { ["signing_key_id"] = key.KeyId });
        return builder.CreateAndSignWithPrivateKey(key.EncryptedPrivateKey, key.Passphrase).ToString();
    }

    private async Task<IssuedLicense> CreateLicenseAsync(Guid sourceOrderId, Guid subscriptionId, Guid tenantId, string planId, string planVersion, IReadOnlyDictionary<string, string> features, DateTimeOffset now, TimeSpan validity, CancellationToken cancellationToken)
    {
        var key = await keys.GetCurrentAsync(cancellationToken)
            ?? throw new InvalidOperationException("License signing key is unavailable.");
        var expiresAt = now.Add(validity);
        var licenseId = Guid.NewGuid();
        var document = CreateDocument(licenseId, sourceOrderId, subscriptionId, tenantId, planId, planVersion, features, expiresAt, key);
        return new IssuedLicense(licenseId, sourceOrderId, subscriptionId, tenantId, planId, planVersion, now, expiresAt, document, key.KeyId, false);
    }
}
