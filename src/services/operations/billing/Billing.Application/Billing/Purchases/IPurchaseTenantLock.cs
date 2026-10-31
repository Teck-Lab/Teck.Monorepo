namespace Billings.Application.Billing.Purchases;

/// <summary>Serializes Billing state mutations for one global tenant.</summary>
public interface IPurchaseTenantLock
{
    /// <summary>Acquires the PostgreSQL transaction-scoped lock shared by all tenant purchase writers and purge gates.</summary>
    /// <param name="tenantId">The globally unique tenant.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A task that completes when the transaction-scoped lock is acquired.</returns>
    Task<bool> AcquireAsync(Guid tenantId, CancellationToken cancellationToken);

    /// <summary>Acquires the same tenant lock without denying the active grant being completed.</summary>
    /// <param name="tenantId">The globally unique tenant.</param>
    /// <param name="cancellationToken">Cancellation signal.</param>
    /// <returns>A task that completes after the transaction owns the lock.</returns>
    Task AcquireForCompletionAsync(Guid tenantId, CancellationToken cancellationToken);
}
