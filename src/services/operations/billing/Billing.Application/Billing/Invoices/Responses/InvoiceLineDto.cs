namespace Billings.Application.Billing.Invoices.Responses;

/// <summary>A single line item of an invoice.</summary>
/// <param name="Id">The invoice line identifier.</param>
/// <param name="ProductId">The identifier of the product this line refers to.</param>
/// <param name="Description">The line description.</param>
/// <param name="Quantity">The quantity billed.</param>
/// <param name="UnitPriceAmount">The unit price amount.</param>
/// <param name="UnitPriceCurrency">The ISO currency code of the unit price.</param>
public sealed record InvoiceLineDto(
    Guid Id,
    Guid ProductId,
    string Description,
    int Quantity,
    decimal UnitPriceAmount,
    string UnitPriceCurrency);
