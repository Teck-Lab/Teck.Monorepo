namespace Billings.Application.Billing.Payments;

/// <summary>The outcome of a payment capture attempt against a provider.</summary>
/// <param name="Success">Whether the capture succeeded.</param>
/// <param name="ProviderReference">The tokenized provider reference when successful.</param>
/// <param name="FailureReason">The private failure reason when unsuccessful.</param>
public sealed record PaymentProviderResult(bool Success, string? ProviderReference, string? FailureReason)
{
    /// <summary>Gets the normalized provider outcome.</summary>
    public string Outcome { get; init; } = Success ? "succeeded" : "failed";

    /// <summary>Gets the billing-private provider code, if one was returned.</summary>
    public string? ProviderCode { get; init; }
}
