namespace Catalog.Domain.ValueObjects;

/// <summary>A name/value descriptor for a variant (e.g. Size = Large).</summary>
public sealed record VariantAttribute(string Name, string Value);
