using Catalog.Application.Products.Responses;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.GetProduct.V1;

/// <summary>Fetches a product by id.</summary>
public sealed record GetProductQuery(Guid ProductId) : IQuery<ProductDto>;
