using SharedKernel.Core.CQRS;

namespace Orders.Application.Orders.Features.RetryPayment.V1;

/// <summary>Invokes a payment retry with an outcome that can be returned through Wolverine.</summary>
public sealed record RetryPaymentInvocationCommand(Guid OrderId, string RequestId, string PaymentMethodToken) : ICommand<RetryPaymentInvocationResult>;
