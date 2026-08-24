namespace Billings.Application.Billing;

/// <summary>
/// Options controlling how the billing service integrates with the payment provider.
/// </summary>
public sealed class PaymentProviderOptions
{
    /// <summary>The configuration section name these options bind from.</summary>
    public const string SectionName = "PaymentProvider";

    /// <summary>Gets a value indicating whether the provider should simulate successful payments.</summary>
    public bool SimulateSuccess { get; init; } = true;

    /// <summary>Gets the name of the configured payment provider.</summary>
    public string ProviderName { get; init; } = "stub";

    /// <summary>
    /// Gets the ISO currency code applied to payments captured from events that do not carry a
    /// currency. <see cref="SharedKernel.Events.OrderPlacedIntegrationEvent"/> currently has no
    /// currency field, so the <c>OrderPlaced</c> consumer falls back to this default until that
    /// contract is extended.
    /// </summary>
    public string DefaultCurrency { get; init; } = "USD";

    /// <summary>Gets the maximum number of automatic transient retries after the initial attempt.</summary>
    public int MaxTransientRetries { get; init; } = 2;

    /// <summary>Gets deterministic stub outcome selected for this environment.</summary>
    public string StubOutcome { get; init; } = "succeeded";

    /// <summary>Gets the provider decline code returned by the deterministic stub.</summary>
    public string StubDeclineCode { get; init; } = "generic_decline";

    /// <summary>Gets reloadable environment mappings from provider codes to shopper-safe categories.</summary>
    public Dictionary<string, string> DeclineMappings { get; init; } = new(StringComparer.OrdinalIgnoreCase);
}
