using FastEndpoints;
using FluentValidation;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="ArchivePriceListRequest"/>.</summary>
public sealed class ArchivePriceListRequestValidator : Validator<ArchivePriceListRequest>
{
    /// <summary>Initializes a new instance of the <see cref="ArchivePriceListRequestValidator"/> class.</summary>
    public ArchivePriceListRequestValidator() =>
        RuleFor(request => request.Id).NotEmpty();
}
