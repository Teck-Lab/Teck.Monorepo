namespace Catalog.Application.Products.Features.AddVariant.V1;

/// <summary>A variant attribute supplied on the request.</summary>
/// <param name="Name">The attribute name (for example, "Color").</param>
/// <param name="Value">The attribute value (for example, "Red").</param>
public sealed record VariantAttributeInput(string Name, string Value);
