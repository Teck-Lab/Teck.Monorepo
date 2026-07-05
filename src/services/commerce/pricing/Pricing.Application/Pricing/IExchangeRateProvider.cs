namespace Pricing.Application.Pricing;

/// <summary>
/// Seam for fetching exchange rates from an external source. v1 uses a no-op stub; a real
/// ECB/OXR adapter and a scheduled refresh can adopt this later without a domain change.
/// </summary>
public interface IExchangeRateProvider
{
    /// <summary>Gets the latest available rates.</summary>
    /// <param name="ct">A cancellation token.</param>
    /// <returns>The latest rate snapshots (empty when no external source is configured).</returns>
    Task<IReadOnlyList<RateSnapshot>> GetLatestAsync(CancellationToken ct);
}
