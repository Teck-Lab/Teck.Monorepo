using Billings.Domain.ValueObjects;
using SharedKernel.Core.Domain;

namespace Billings.Domain.Entities;

/// <summary>A single line item of an <see cref="Invoice"/>. Owned by the invoice aggregate.</summary>
public sealed class InvoiceLine : BaseEntity
{
    private InvoiceLine()
    {
    }

    /// <summary>Gets the identifier of the product this line refers to.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Gets the line description.</summary>
    public string Description { get; private set; } = string.Empty;

    /// <summary>Gets the quantity billed.</summary>
    public int Quantity { get; private set; }

    /// <summary>Gets the unit price.</summary>
    public Money UnitPrice { get; private set; } = null!;

    /// <summary>Creates a new invoice line.</summary>
    /// <param name="productId">The identifier of the product this line refers to.</param>
    /// <param name="description">The line description.</param>
    /// <param name="quantity">The quantity billed (must be positive).</param>
    /// <param name="unitPrice">The unit price.</param>
    /// <returns>The newly created invoice line.</returns>
    internal static InvoiceLine Create(Guid productId, string description, int quantity, Money unitPrice)
    {
        ArgumentNullException.ThrowIfNull(unitPrice);

        if (productId == Guid.Empty)
        {
            throw new ArgumentException("ProductId is required.", nameof(productId));
        }

        if (string.IsNullOrWhiteSpace(description))
        {
            throw new ArgumentException("Description is required.", nameof(description));
        }

        if (quantity <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be positive.");
        }

        return new InvoiceLine
        {
            ProductId = productId,
            Description = description,
            Quantity = quantity,
            UnitPrice = unitPrice,
        };
    }
}
