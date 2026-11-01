using Licensing.Application;

namespace Licensing.Host;

/// <summary>Validates the caller-provided document against Licensing's durable revocation store.</summary>
public static class LicenseVerificationEndpoint
{
    /// <summary>Checks signature, expiry, persisted identity and revocation using runtime trusted public keys.</summary>
    /// <param name="request">The claimed tenant and signed license document.</param>
    /// <param name="issuer">The persistent license validator.</param>
    /// <param name="configuration">Runtime public key configuration, including rotated trusted keys.</param>
    /// <param name="http">The response headers.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A sanitized verification result with entitlements only when valid.</returns>
    public static async Task<IResult> VerifyAsync(
        LicenseVerificationRequest request,
        LicenseIssuer issuer,
        IConfiguration configuration,
        HttpContext http,
        CancellationToken cancellationToken)
    {
        http.Response.Headers.CacheControl = "no-store";
        var trustedKeys = configuration.GetSection("LicenseSigning:TrustedPublicKeys").Get<string[]>() ?? [];
        var result = await issuer.ValidateAsync(request.LicenseDocument, request.TenantId,
            DateTimeOffset.UtcNow, trustedKeys, cancellationToken).ConfigureAwait(false);
        if (!result.IsValid || result.License is null)
        {
            return Results.Ok(new LicenseVerificationResponse(false));
        }

        var license = result.License;
        return Results.Ok(new LicenseVerificationResponse(true, license.LicenseId, license.SourceOrderId,
            license.SubscriptionId, license.PlanId, license.PlanVersion, license.ExpiresAt, result.Features));
    }
}
