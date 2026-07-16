using Billings.Application.Billing.Invoices.Mapping;
using Billings.Application.Billing.Invoices.ReadModels;
using Billings.Application.Billing.Invoices.Responses;
using Billings.Domain.Entities;
using ErrorOr;
using SharedKernel.Core.Database;

namespace Billings.Application.Billing.Invoices.Features.GetInvoice.V1;

/// <summary>Handles <see cref="GetInvoiceQuery"/>.</summary>
public static class GetInvoiceHandler
{
    /// <summary>Returns the invoice DTO or a NotFound error.</summary>
    /// <param name="query">The query identifying the invoice to return.</param>
    /// <param name="repository">The repository used to load the invoice.</param>
    /// <param name="ct">The cancellation token.</param>
    /// <returns>A task resolving to the invoice DTO or a NotFound error.</returns>
    public static async Task<ErrorOr<InvoiceDto>> Handle(
        GetInvoiceQuery query,
        IGenericReadRepository<Invoice, Guid> repository,
        CancellationToken ct)
    {
        var invoice = await repository.FirstOrDefaultAsync(new InvoiceByIdSpec(query.InvoiceId), ct).ConfigureAwait(false);

        return invoice is null
            ? Error.NotFound(description: $"Invoice '{query.InvoiceId}' was not found.")
            : invoice.ToDto();
    }
}
