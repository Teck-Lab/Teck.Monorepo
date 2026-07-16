using Billings.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Billings.Domain.Entities;

/// <summary>
/// Represents an invoice aggregate root issued for a customer order.
/// </summary>
public sealed class Invoice : BaseEntity, IAggregateRoot, ITenantScoped
{
    private readonly List<InvoiceLine> _lines = [];

    private Invoice()
    {
    }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Gets the identifier of the order this invoice was issued for.</summary>
    public Guid OrderId { get; private set; }

    /// <summary>Gets the timestamp at which the invoice was issued.</summary>
    public DateTimeOffset IssuedAt { get; private set; }

    /// <summary>Gets the total invoiced amount.</summary>
    public Money Amount { get; private set; } = null!;

    /// <summary>Gets the invoice lines.</summary>
    public IReadOnlyList<InvoiceLine> Lines => _lines;

    /// <summary>Creates a new invoice.</summary>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <param name="orderId">The identifier of the order this invoice was issued for.</param>
    /// <param name="total">The total invoiced amount.</param>
    /// <param name="lines">The invoice lines.</param>
    /// <param name="issuedAt">The timestamp at which the invoice was issued.</param>
    /// <returns>The newly created invoice.</returns>
    public static Invoice Create(string tenantId, Guid orderId, Money total, IEnumerable<InvoiceLine> lines, DateTimeOffset issuedAt)
    {
        ArgumentNullException.ThrowIfNull(total);
        ArgumentNullException.ThrowIfNull(lines);

        if (orderId == Guid.Empty)
        {
            throw new ArgumentException("OrderId is required.", nameof(orderId));
        }

        var invoice = new Invoice
        {
            TenantId = tenantId,
            OrderId = orderId,
            Amount = total,
            IssuedAt = issuedAt,
        };

        invoice._lines.AddRange(lines);

        if (invoice._lines.Count == 0)
        {
            throw new ArgumentException("Invoice must contain at least one line.", nameof(lines));
        }

        return invoice;
    }
}
