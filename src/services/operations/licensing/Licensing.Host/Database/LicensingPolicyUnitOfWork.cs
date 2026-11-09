using System.Data;
using Licensing.Application.Database;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using SharedKernel.Core.Database;

namespace Licensing.Host.Database;

/// <summary>Adapts the Licensing EF context to the shared unit-of-work contract.</summary>
/// <param name="context">The scoped Licensing write context.</param>
public sealed class LicensingPolicyUnitOfWork(LicensingDbContext context) : IUnitOfWork
{
    private IDbContextTransaction? transaction;

    /// <inheritdoc />
    public async Task<IDbTransaction> BeginTransactionAsync(IsolationLevel isolationLevel = IsolationLevel.ReadCommitted,
        CancellationToken cancellationToken = default)
    {
        if (transaction is not null)
        {
            throw new InvalidOperationException("A transaction has already been started.");
        }

        transaction = await context.Database.BeginTransactionAsync(isolationLevel, cancellationToken).ConfigureAwait(false);
        return transaction.GetDbTransaction();
    }

    /// <inheritdoc />
    public async Task CommitTransactionAsync(CancellationToken cancellationToken = default)
    {
        var activeTransaction = transaction ?? throw new InvalidOperationException("A transaction has not been started.");
        await activeTransaction.CommitAsync(cancellationToken).ConfigureAwait(false);
        await activeTransaction.DisposeAsync().ConfigureAwait(false);
        transaction = null;
    }

    /// <inheritdoc />
    public async Task RollbackTransactionAsync(CancellationToken cancellationToken = default)
    {
        var activeTransaction = transaction ?? throw new InvalidOperationException("A transaction has not been started.");
        try
        {
            await activeTransaction.RollbackAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            await activeTransaction.DisposeAsync().ConfigureAwait(false);
            transaction = null;
        }
    }

    /// <inheritdoc />
    public Task<int> SaveChangesAsync(CancellationToken cancellationToken = default) => context.SaveChangesAsync(cancellationToken);

    /// <inheritdoc />
    public void Dispose()
    {
        transaction?.Dispose();
        transaction = null;
    }
}
