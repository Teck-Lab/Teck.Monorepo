namespace Catalog.Application.Options;

/// <summary>Service configuration for the catalog (bound via the Options pattern).</summary>
public sealed class CatalogOptions
{
    /// <summary>The configuration section name.</summary>
    public const string SectionName = "Catalog";

    /// <summary>The default ISO currency code used when none is supplied.</summary>
    public string DefaultCurrency { get; set; } = "USD";
}
