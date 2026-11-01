namespace SharedKernel.Events;

/// <summary>Confirms an attached policy remains active in Licensing at the point of use.</summary>
public interface IEnterprisePolicyFreshnessVerifier
{
    /// <summary>Looks up the exact transition and requires the current signed document.</summary>
    /// <param name="transition">The Billing-attested policy transition.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A task that completes only when the active Licensing document matches.</returns>
    Task VerifyAsync(EnterprisePolicyAttachedIntegrationEvent transition, CancellationToken cancellationToken);
}
