namespace Pricing.Domain.ValueObjects;

/// <summary>The kind of change described by a price-changed event.</summary>
public enum PriceChangeType
{
    /// <summary>A price was created or updated and is (or becomes) effective.</summary>
    Upserted,

    /// <summary>A price was removed or retracted and is no longer effective.</summary>
    Removed,
}
