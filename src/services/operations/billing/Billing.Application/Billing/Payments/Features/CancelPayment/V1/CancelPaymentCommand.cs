using MemoryPack;
using SharedKernel.Core.CQRS;

namespace Billings.Application.Billing.Payments.Features.CancelPayment.V1;

/// <summary>Requests idempotent cancellation of a pending payment.</summary>
/// <param name="OrderId">The order identifier.</param>
/// <param name="RequestId">The stable cancellation request identifier.</param>
[MemoryPackable]
public sealed partial record CancelPaymentCommand(Guid OrderId, string RequestId) : ICommand;
