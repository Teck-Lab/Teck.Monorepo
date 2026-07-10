using ErrorOr;
using Pricing.Application.Pricing.Responses;
using SharedKernel.Core.CQRS;

namespace Pricing.Application.Pricing.Features.ArchivePriceList.V1;

/// <summary>Command that archives a price list.</summary>
/// <param name="Id">The list identifier.</param>
public sealed record ArchivePriceListCommand(Guid Id) : ICommand<ErrorOr<PriceListDto>>;
