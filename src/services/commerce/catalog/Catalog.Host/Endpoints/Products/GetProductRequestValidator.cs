using FastEndpoints;
using FluentValidation;

namespace Catalog.Host.Endpoints.Products;

/// <summary>Validates <see cref="GetProductRequest"/> instances.</summary>
public sealed class GetProductRequestValidator : Validator<GetProductRequest>
{
    /// <summary>Initializes a new instance of the <see cref="GetProductRequestValidator"/> class.</summary>
    public GetProductRequestValidator() => RuleFor(request => request.ProductId).NotEmpty();
}
