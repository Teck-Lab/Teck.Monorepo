using FastEndpoints;
using FluentValidation;
using Pricing.Application.Pricing.Features.AddOrUpdatePrice.V1;

namespace Pricing.Host.Endpoints.Pricing;

/// <summary>Validates <see cref="AddOrUpdatePriceRequest"/>.</summary>
public sealed class AddOrUpdatePriceRequestValidator : Validator<AddOrUpdatePriceRequest>
{
    /// <summary>Initializes a new instance of the <see cref="AddOrUpdatePriceRequestValidator"/> class.</summary>
    public AddOrUpdatePriceRequestValidator()
    {
        RuleFor(request => request.Id).NotEmpty();
        RuleFor(request => request.ProductId).NotEmpty();
        RuleFor(request => request.Amount).GreaterThanOrEqualTo(0);

        RuleForEach(request => request.Tiers).ChildRules(tier =>
        {
            tier.RuleFor(t => t.MinQuantity).GreaterThanOrEqualTo(1);
            tier.RuleFor(t => t.Amount).GreaterThanOrEqualTo(0);
        });

        RuleFor(request => request.Tiers)
            .Must(HaveStrictlyAscendingUniqueMinQuantities)
            .WithMessage("Tiers must have strictly ascending, unique minimum quantities.");
    }

    private static bool HaveStrictlyAscendingUniqueMinQuantities(IReadOnlyList<PriceTierInput>? tiers)
    {
        if (tiers is null || tiers.Count == 0)
        {
            return true;
        }

        int previousMin = 0;
        bool isFirst = true;
        foreach (PriceTierInput tier in tiers)
        {
            if (!isFirst && tier.MinQuantity <= previousMin)
            {
                return false;
            }

            previousMin = tier.MinQuantity;
            isFirst = false;
        }

        return true;
    }
}
