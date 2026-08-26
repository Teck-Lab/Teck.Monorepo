namespace Orders.Application.Orders.Features.RetryPayment.V1;

/// <summary>Describes the externally actionable result of a payment retry invocation.</summary>
public enum RetryPaymentInvocationOutcome
{
    /// <summary>The retry was accepted or was an idempotent replay.</summary>
    Accepted,

    /// <summary>The order is not visible to the current tenant.</summary>
    NotFound,

    /// <summary>The retry request is invalid for the current order state.</summary>
    Invalid,
}
