namespace Billings.Host.Endpoints.Invoices;

/// <summary>Request to fetch an invoice by identifier.</summary>
/// <param name="InvoiceId">The invoice identifier.</param>
public sealed record GetInvoiceRequest(Guid InvoiceId);
