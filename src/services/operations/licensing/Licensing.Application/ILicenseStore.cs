namespace Licensing.Application;

/// <summary>Persistence boundary for licenses; subscription identity is idempotent.</summary>
public interface ILicenseStore
{
    /// <summary>Finds the active subscription license or the latest revoked tombstone.</summary>
    /// <param name="subscriptionId">The subscription identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The active license, the most recent revoked license, or null when absent.</returns>
    Task<IssuedLicense?> FindBySubscriptionAsync(Guid subscriptionId, CancellationToken cancellationToken);

    /// <summary>Finds a license by its signed document ID.</summary>
    /// <param name="licenseId">The signed license identifier.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>The matching license, or null when absent.</returns>
    Task<IssuedLicense?> FindByLicenseIdAsync(Guid licenseId, CancellationToken cancellationToken);

    /// <summary>Persists lifecycle changes.</summary>
    /// <param name="license">The updated license record.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task for the persistence operation.</returns>
    Task UpdateAsync(IssuedLicense license, CancellationToken cancellationToken);

    /// <summary>Atomically revokes a license and stores its replacement.</summary>
    /// <param name="licenseId">The license being replaced.</param>
    /// <param name="replacement">The new active license.</param>
    /// <param name="cancellationToken">The cancellation token.</param>
    /// <returns>A task for the transaction.</returns>
    Task ReplaceAsync(Guid licenseId, IssuedLicense replacement, CancellationToken cancellationToken);

    /// <summary>Whether Billing suspended this particular paid source order, including out-of-order cancellation.</summary>
    /// <param name="sourceOrderId">The signed Billing order identity.</param>
    /// <param name="subscriptionId">The stable subscription identity.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>True after an authenticated period-specific suspension was recorded.</returns>
    Task<bool> IsSuspendedAsync(Guid sourceOrderId, Guid subscriptionId, CancellationToken cancellationToken);

    /// <summary>Persists a signed license only if this subscription has not been suspended, atomically with that check.</summary>
    /// <param name="license">The verified-payment signed license.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A task for the persistence operation.</returns>
    Task AddUnlessSuspendedAsync(IssuedLicense license, CancellationToken cancellationToken);

    /// <summary>Atomically records a suspension tombstone and revokes any active license for this subscription.</summary>
    /// <param name="sourceOrderId">The originating Billing order.</param>
    /// <param name="subscriptionId">The suspended subscription.</param>
    /// <param name="tenantId">The owning Customer tenant.</param>
    /// <param name="suspendedAtUtc">The effective suspension time.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A task for the atomic suspension operation.</returns>
    Task SuspendAsync(Guid sourceOrderId, Guid subscriptionId, Guid tenantId, DateTimeOffset suspendedAtUtc, CancellationToken cancellationToken);
}
