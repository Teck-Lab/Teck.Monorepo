using FastEndpoints;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="ListProductsRequest"/> instances.</summary>
public sealed class ListProductsRequestValidator : Validator<ListProductsRequest>
{
    /// <summary>Initializes a new instance of the <see cref="ListProductsRequestValidator"/> class.</summary>
    public ListProductsRequestValidator()
    {
    }
}
