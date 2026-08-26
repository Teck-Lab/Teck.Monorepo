using Billings.Application.Billing.Payments.Responses;
using ErrorOr;
using MemoryPack;
using SharedKernel.Core.CQRS;

namespace Billings.Application.Billing.Payments.Features.RetryPayment.V1;

/// <summary>Requests an idempotent payment retry with a tokenized payment method.</summary>
/// <param name="OrderId">The order identifier.</param>
/// <param name="AuthorizedAmount">The shopper-authorized ceiling.</param>
/// <param name="Currency">The authorized currency.</param>
/// <param name="PaymentMethodToken">The replacement opaque token.</param>
/// <param name="RequestId">The stable retry request identifier.</param>
/// <param name="SourceCorrelationId">The source correlation identifier.</param>
[MemoryPackable]
public sealed partial record RetryPaymentCommand(Guid OrderId, decimal AuthorizedAmount, string Currency, string PaymentMethodToken, string RequestId, string SourceCorrelationId) : ICommand<ErrorOr<PaymentDto>>;
