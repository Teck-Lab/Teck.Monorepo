using Billings.Application.Billing.Invoices.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Billings.Application.Billing.Invoices.Features.GetInvoice.V1;

/// <summary>Fetches an invoice by id.</summary>
/// <param name="InvoiceId">The identifier of the invoice to fetch.</param>
public sealed record GetInvoiceQuery(Guid InvoiceId) : IQuery<ErrorOr<InvoiceDto>>;
