using Billings.Domain.ValueObjects;

namespace Billings.Application.Billing.Payments.Features.ProcessPaymentOutcome.V1;

/// <summary>Normalizes private provider outcomes into aggregate-safe values.</summary>
public static class PaymentOutcomeSupport
{
    /// <summary>Normalizes a provider result.</summary>
    /// <param name="result">The provider result to normalize.</param>
    /// <param name="resolver">The reloadable decline category resolver.</param>
    /// <returns>The normalized safe outcome.</returns>
    public static PaymentOutcome Normalize(PaymentProviderResult result, DeclineCategoryResolver resolver)
    {
        var outcome = result.Outcome.Trim().ToLowerInvariant();
        return outcome switch
        {
            "succeeded" => new PaymentOutcome(PaymentAttemptStatus.Succeeded, null, null),
            "processing" => new PaymentOutcome(PaymentAttemptStatus.Processing, null, null),
            "requires_action" => new PaymentOutcome(PaymentAttemptStatus.RequiresAction, DeclineCategory.AuthenticationRequired, null),
            "requires_payment_method" => new PaymentOutcome(PaymentAttemptStatus.RequiresPaymentMethod, DeclineCategory.PaymentMethodRequired, null),
            _ => Declined(result, resolver),
        };
    }

    /// <summary>Produces the shopper-safe text sent in a failure event.</summary>
    /// <param name="category">The safe decline category.</param>
    /// <returns>The safe shopper action text.</returns>
    public static string ActionText(DeclineCategory category) => category == DeclineCategory.AuthenticationRequired
        ? "Complete payment authentication."
        : category == DeclineCategory.PaymentMethodRequired
            ? "Use a different payment method."
            : category == DeclineCategory.IssuerContactRequired
                ? "Contact your card issuer."
                : "Payment was declined.";

    private static PaymentOutcome Declined(PaymentProviderResult result, DeclineCategoryResolver resolver)
    {
        var resolution = resolver.Resolve(result.ProviderCode ?? result.FailureReason);
        return new PaymentOutcome(PaymentAttemptStatus.Failed, resolution.Category, resolution.AuditHash);
    }
}
