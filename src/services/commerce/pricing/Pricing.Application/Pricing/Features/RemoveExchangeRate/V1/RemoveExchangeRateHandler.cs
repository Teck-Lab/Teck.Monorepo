using ErrorOr;
using Pricing.Application.Pricing.ReadModels;
using Pricing.Domain.Entities;
using SharedKernel.Core.Database;

namespace Pricing.Application.Pricing.Features.RemoveExchangeRate.V1;

/// <summary>Handles <see cref="RemoveExchangeRateCommand"/>.</summary>
public static class RemoveExchangeRateHandler
{
    /// <summary>Removes the rate for the pair, or returns not-found.</summary>
    /// <param name="command">The command.</param>
    /// <param name="repository">The write repository.</param>
    /// <param name="unitOfWork">The unit of work.</param>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>Success, or a not-found error.</returns>
    public static async Task<ErrorOr<Success>> Handle(
        RemoveExchangeRateCommand command,
        IGenericWriteRepository<ExchangeRate, Guid> repository,
        IUnitOfWork unitOfWork,
        CancellationToken ct)
    {
        ExchangeRate? rate = await repository.FirstOrDefaultAsync(
            new ExchangeRateByPairSpec(command.FromCurrency, command.ToCurrency), enableTracking: true, ct).ConfigureAwait(false);
        if (rate is null)
        {
            return Error.NotFound(description: $"Exchange rate '{command.FromCurrency}->{command.ToCurrency}' was not found.");
        }

        repository.Delete(rate);
        await unitOfWork.SaveChangesAsync(ct).ConfigureAwait(false);
        return Result.Success;
    }
}
