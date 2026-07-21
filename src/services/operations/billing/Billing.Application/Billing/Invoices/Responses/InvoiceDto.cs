namespace Billings.Application.Billing.Invoices.Responses;

/// <summary>An invoice issued for a customer order.</summary>
/// <param name="Id">The invoice identifier.</param>
/// <param name="OrderId">The identifier of the order this invoice was issued for.</param>
/// <param name="Amount">The total invoiced amount.</param>
/// <param name="Currency">The ISO currency code of the total invoiced amount.</param>
/// <param name="IssuedAt">The timestamp at which the invoice was issued.</param>
/// <param name="Lines">The invoice lines.</param>
public sealed record InvoiceDto(
    Guid Id,
    Guid OrderId,
    decimal Amount,
    string Currency,
    DateTimeOffset IssuedAt,
    IReadOnlyList<InvoiceLineDto> Lines);
