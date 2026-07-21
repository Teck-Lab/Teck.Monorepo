using Ardalis.Specification;
using Billings.Domain.Entities;

namespace Billings.Application.Billing.Invoices.ReadModels;

/// <summary>Selects a single invoice (with its lines) by id.</summary>
public sealed class InvoiceByIdSpec : Specification<Invoice>
{
    /// <summary>Initializes the spec.</summary>
    /// <param name="invoiceId">The identifier of the invoice to select.</param>
    public InvoiceByIdSpec(Guid invoiceId) => Query.Where(invoice => invoice.Id == invoiceId).Include(invoice => invoice.Lines);
}
