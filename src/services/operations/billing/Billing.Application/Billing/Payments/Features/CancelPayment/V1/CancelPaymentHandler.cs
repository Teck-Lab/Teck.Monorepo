using Billings.Application.Billing.Payments.ReadModels;
using Billings.Domain.Entities;
using SharedKernel.Core.Database;

namespace Billings.Application.Billing.Payments.Features.CancelPayment.V1;

/// <summary>Cancels an uncaptured payment without producing a provider call.</summary>
public static class CancelPaymentHandler
{
    /// <summary>Handles a cancellation request idempotently.</summary>
    /// <param name="command">The cancellation command.</param>
    /// <param name="payments">The tracked payment repository.</param>
    /// <param name="unitOfWork">The commit boundary.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task that completes after cancellation.</returns>
    public static async Task Handle(CancelPaymentCommand command, IGenericWriteRepository<Payment, Guid> payments, IUnitOfWork unitOfWork, CancellationToken ct)
    {
        var payment = await payments.FirstOrDefaultAsync(new PaymentByOrderSpec(command.OrderId), enableTracking: true, ct).ConfigureAwait(false);
        if (payment is null || payment.Status == PaymentStatus.Captured || payment.CancellationRequestId == command.RequestId)
        {
            return;
        }

        payment.Cancel(command.RequestId);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
    }
}
