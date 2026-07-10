using Pricing.Application.Pricing;

namespace Pricing.Host.Infrastructure;

/// <summary>
/// No-op <see cref="IExchangeRateProvider"/>: returns no rates. A real ECB/OXR adapter and a
/// scheduled refresh can replace this without any domain or application change.
/// </summary>
public sealed class ExchangeRateProviderStub : IExchangeRateProvider
{
    /// <inheritdoc/>
    public Task<IReadOnlyList<RateSnapshot>> GetLatestAsync(CancellationToken ct) =>
        Task.FromResult<IReadOnlyList<RateSnapshot>>([]);
}
