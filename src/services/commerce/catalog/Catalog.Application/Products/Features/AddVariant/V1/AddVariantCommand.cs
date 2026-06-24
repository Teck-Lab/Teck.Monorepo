using Catalog.Application.Products.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.AddVariant.V1;

/// <summary>A variant attribute supplied on the request.</summary>
public sealed record VariantAttributeInput(string Name, string Value);

/// <summary>Adds a non-default variant to an existing product.</summary>
public sealed record AddVariantCommand(
    Guid ProductId,
    string Sku,
    decimal SellPriceAmount,
    string SellPriceCurrency,
    IReadOnlyList<VariantAttributeInput> Attributes) : ICommand<ErrorOr<VariantDto>>;
