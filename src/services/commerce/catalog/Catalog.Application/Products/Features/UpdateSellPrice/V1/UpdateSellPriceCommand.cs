using Catalog.Application.Products.Responses;
using ErrorOr;
using SharedKernel.Core.CQRS;

namespace Catalog.Application.Products.Features.UpdateSellPrice.V1;

/// <summary>Changes a variant's sell price.</summary>
public sealed record UpdateSellPriceCommand(Guid ProductId, Guid VariantId, decimal Amount, string Currency)
    : ICommand<ErrorOr<VariantDto>>;
