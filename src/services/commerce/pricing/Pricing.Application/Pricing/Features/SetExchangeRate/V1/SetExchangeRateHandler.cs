using Finbuckle.MultiTenant.Abstractions;
using Pricing.Application.Pricing.Mapping;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Application.Pricing.Responses;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.SetExchangeRate.V1;

/// <summary>Handles <see cref="SetExchangeRateCommand"/> with upsert-by-pair semantics.</summary>
public static class SetExchangeRateHandler
{
    /// <summary>Creates or updates the rate for the pair and commits.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="tenant">The current tenant.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The saved exchange rate.</returns>
    public static async Task<ExchangeRateDto> Handle(
        SetExchangeRateCommand command,
        IGenericWriteRepository<ExchangeRate, Guid> repository,
        IUnitOfWork unitOfWork,
        ITenantInfo tenant,
        CancellationToken ct)
    {
        ExchangeRate? rate = await repository.FirstOrDefaultAsync(
            new ExchangeRateByPairSpec(command.FromCurrency, command.ToCurrency), enableTracking: true, ct).ConfigureAwait(false);

        if (rate is null)
        {
            rate = ExchangeRate.Create(command.FromCurrency, command.ToCurrency, command.Rate, command.ValidFrom, command.ValidUntil, tenant.Id ?? string.Empty);
            await repository.AddAsync(rate, ct).ConfigureAwait(false);
        }
        else
        {
            rate.UpdateRate(command.Rate);
            rate.UpdateValidity(command.ValidFrom, command.ValidUntil);
        }

        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return rate.ToDto();
    }
}
