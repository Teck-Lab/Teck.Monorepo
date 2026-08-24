using SharedKernel.Core.Domain;

namespace Pricing.Domain.Entities;

/// <summary>Persists one bounded checkout awaiting a catalog fallback reconciliation response.</summary>
public sealed class PendingPriceResolution : BaseEntity, IAggregateRoot, ITenantScoped
{
    private PendingPriceResolution()
    {
    }

    /// <summary>Gets the missing catalog product identifier.</summary>
    public Guid ProductId { get; private set; }

    /// <summary>Gets the basket to price after reconciliation.</summary>
    public Guid BasketId { get; private set; }

    /// <summary>Gets the shopper-authorized ceiling.</summary>
    public decimal AuthorizedAmount { get; private set; }

    /// <summary>Gets the requested checkout currency.</summary>
    public string Currency { get; private set; } = string.Empty;

    /// <summary>Gets the stable pricing request key.</summary>
    public string RequestId { get; private set; } = string.Empty;

    /// <summary>Gets the original checkout correlation identifier.</summary>
    public string SourceCorrelationId { get; private set; } = string.Empty;

    /// <summary>Gets the serialized checkout lines needed to resume pricing.</summary>
    public string LinesJson { get; private set; } = string.Empty;

    /// <summary>Gets a value indicating whether the pending request has been resumed.</summary>
    public bool IsResolved { get; private set; }

    /// <inheritdoc/>
    public string TenantId { get; set; } = string.Empty;

    /// <summary>Creates a pending checkout resolution.</summary>
    /// <param name="productId">The product awaiting a fallback price.</param>
    /// <param name="basketId">The checkout basket identifier.</param>
    /// <param name="authorizedAmount">The shopper authorization ceiling.</param>
    /// <param name="currency">The requested ISO currency.</param>
    /// <param name="requestId">The stable pricing request key.</param>
    /// <param name="sourceCorrelationId">The originating checkout correlation identifier.</param>
    /// <param name="linesJson">The serialized checkout lines.</param>
    /// <param name="tenantId">The owning tenant identifier.</param>
    /// <returns>The pending reconciliation row.</returns>
    public static PendingPriceResolution Create(
        Guid productId,
        Guid basketId,
        decimal authorizedAmount,
        string currency,
        string requestId,
        string sourceCorrelationId,
        string linesJson,
        string tenantId) => new()
    {
        ProductId = productId,
        BasketId = basketId,
        AuthorizedAmount = authorizedAmount,
        Currency = currency.ToUpperInvariant(),
        RequestId = requestId,
        SourceCorrelationId = sourceCorrelationId,
        LinesJson = linesJson,
        TenantId = tenantId,
    };

    /// <summary>Advances this bounded request to its next uncovered product.</summary>
    /// <param name="productId">The next catalog product requiring reconciliation.</param>
    public void AwaitProduct(Guid productId) => ProductId = productId;

    /// <summary>Marks this request resolved so redelivery cannot publish a duplicate result.</summary>
    public void MarkResolved() => IsResolved = true;
}
