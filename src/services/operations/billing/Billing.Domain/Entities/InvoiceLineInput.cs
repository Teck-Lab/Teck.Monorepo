using Billings.Domain.ValueObjects;

namespace Billings.Domain.Entities;

/// <summary>
/// Input data used by <see cref="Invoice.Create"/> to build an owned <see cref="InvoiceLine"/>.
/// </summary>
/// <param name="ProductId">The identifier of the product this line refers to.</param>
/// <param name="Description">The line description.</param>
/// <param name="Quantity">The quantity billed.</param>
/// <param name="UnitPrice">The unit price.</param>
public sealed record InvoiceLineInput(Guid ProductId, string Description, int Quantity, Money UnitPrice);
