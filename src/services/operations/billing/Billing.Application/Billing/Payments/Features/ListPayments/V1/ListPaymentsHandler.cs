using Billings.Application.Billing.Payments.Mapping;
using Billings.Application.Billing.Payments.ReadModels;
using Billings.Application.Billing.Payments.Responses;
using Billings.Domain.Entities;
using Finbuckle.MultiTenant.Abstractions;
using SharedKernel.Core.Database;

namespace Billings.Application.Billing.Payments.Features.ListPayments.V1;

/// <summary>Handles <see cref="ListPaymentsQuery"/>.</summary>
public static class ListPaymentsHandler
{
    /// <summary>Returns all payments, most recently created first.</summary>
    /// <param name="query">The query.</param>
    /// <param name="repository">The repository used to load payments.</param>
    /// <param name="tenant">The authenticated tenant authorized to list payments.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task resolving to the payment DTOs.</returns>
    public static async Task<IReadOnlyList<PaymentDto>> Handle(
        ListPaymentsQuery query,
        IGenericReadRepository<Payment, Guid> repository,
        ITenantInfo tenant,
        CancellationToken ct)
    {
        var results = await repository.ListAsync(new AllPaymentsSpec(tenant.Id), ct).ConfigureAwait(false);
        return results.ToDtos();
    }
}
