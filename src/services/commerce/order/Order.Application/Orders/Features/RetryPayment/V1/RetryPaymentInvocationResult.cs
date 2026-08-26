namespace Orders.Application.Orders.Features.RetryPayment.V1;

/// <summary>Contains the typed result of a payment retry invocation.</summary>
/// <param name="Outcome">The retry invocation outcome.</param>
public sealed record RetryPaymentInvocationResult(RetryPaymentInvocationOutcome Outcome);
