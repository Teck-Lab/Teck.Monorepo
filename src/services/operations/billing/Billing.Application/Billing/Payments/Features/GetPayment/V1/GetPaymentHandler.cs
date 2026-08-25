using Billings.Application.Billing.Payments.Mapping;
using Billings.Application.Billing.Payments.ReadModels;
using Billings.Application.Billing.Payments.Responses;
using Billings.Domain.Entities;
using ErrorOr;
using Finbuckle.MultiTenant.Abstractions;
using SharedKernel.Core.Database;

namespace Billings.Application.Billing.Payments.Features.GetPayment.V1;

/// <summary>Handles <see cref="GetPaymentQuery"/>.</summary>
public static class GetPaymentHandler
{
    /// <summary>Returns the payment DTO or a NotFound error.</summary>
    /// <param name="query">The query identifying the payment to return.</param>
    /// <param name="repository">The repository used to load the payment.</param>
    /// <param name="tenant">The authenticated tenant authorized to read the payment.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task resolving to the payment DTO or a NotFound error.</returns>
    public static async Task<ErrorOr<PaymentDto>> Handle(
        GetPaymentQuery query,
        IGenericReadRepository<Payment, Guid> repository,
        ITenantInfo tenant,
        CancellationToken ct)
    {
        var payment = await repository.FirstOrDefaultAsync(new PaymentByIdSpec(query.PaymentId, tenant.Id), ct).ConfigureAwait(false);

        return payment is null
            ? Error.NotFound(description: $"Payment '{query.PaymentId}' was not found.")
            : payment.ToDto();
    }
}
