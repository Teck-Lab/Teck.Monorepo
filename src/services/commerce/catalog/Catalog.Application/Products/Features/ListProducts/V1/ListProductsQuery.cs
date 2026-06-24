using Catalog.Application.Products.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.ListProducts.V1;

/// <summary>Lists products, optionally filtered by category.</summary>
public sealed record ListProductsQuery(Guid? CategoryId) : IQuery<IReadOnlyList<ProductSummaryDto>>;
