using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Orders.Application.Orders.Features.RetryPayment.V1;

/// <summary>Requests an owner-authorized retry with a replacement opaque payment token.</summary>
public sealed record RetryPaymentCommand(Guid OrderId, string RequestId, string PaymentMethodToken) : ICommand<ErrorOr<Success>>;
